const test = require('node:test');
const assert = require('node:assert/strict');
const { eligibility, approve } = require('./approval.cjs');

function fixture() {
  return {
    repository: 'mikidaren/Miki-GrasshopperWarehouse',
    defaultBranch: 'main',
    run: { conclusion: 'success', event: 'pull_request', head_sha: 'abc', path: '.github/workflows/validate.yml' },
    pr: { state: 'open', draft: false, author_association: 'OWNER', user: { type: 'User' },
      head: { sha: 'abc', repo: { full_name: 'mikidaren/Miki-GrasshopperWarehouse' } }, base: { ref: 'main' } },
    files: [{ filename: 'examples/iceworld/straight-stair.json', status: 'modified' }]
  };
}
test('trusted numeric example change at successful current head is eligible', () => assert.equal(eligibility(fixture()).eligible, true));
test('stale validation cannot approve new head', () => { const f = fixture(); f.pr.head.sha = 'new'; assert.equal(eligibility(f).eligible, false); });
test('failed and manually dispatched validation cannot approve', () => {
  const f = fixture(); f.run.conclusion = 'failure'; assert.equal(eligibility(f).eligible, false);
  f.run.conclusion = 'success'; f.run.event = 'workflow_dispatch'; assert.equal(eligibility(f).eligible, false);
});
test('policy, workflow, binaries and executable GH documents require review', () => {
  for (const filename of ['policies/execution.json', '.github/workflows/validate.yml', 'src/code.cs', 'examples/iceworld/a.gha', 'examples/iceworld/a.ghx', 'examples/iceworld/a.py', 'examples/iceworld/../x.json']) {
    const f = fixture(); f.files[0].filename = filename; assert.equal(eligibility(f).eligible, false, filename);
  }
});
test('renames and deletions require review', () => {
  for (const status of ['renamed', 'removed']) { const f = fixture(); f.files[0].status = status; assert.equal(eligibility(f).eligible, false); }
});
test('forks, strangers, bots, drafts and other base branches require review', () => {
  const edits = [f => f.pr.head.repo.full_name = 'other/fork', f => f.pr.author_association = 'NONE',
    f => f.pr.user.type = 'Bot', f => f.pr.draft = true, f => f.pr.base.ref = 'release'];
  for (const edit of edits) { const f = fixture(); edit(f); assert.equal(eligibility(f).eligible, false); }
});
test('unknown validation workflow and empty changes require review', () => {
  const f = fixture(); f.run.path = 'other.yml'; assert.equal(eligibility(f).eligible, false);
  f.run.path = '.github/workflows/validate.yml'; f.files = []; assert.equal(eligibility(f).eligible, false);
});
test('retargeted PR is rechecked before writing approval', async () => {
  const f = fixture(); f.pr.number = 1;
  const reads = [f.pr, { ...f.pr, base: { ref: 'release' } }];
  const written = [];
  const listFiles = async () => {};
  const github = {
    paginate: async method => method === listFiles ? f.files : [],
    rest: { pulls: {
      get: async () => ({ data: reads.shift() }), listFiles, listReviews: async () => {},
      createReview: async review => written.push(review)
    } }
  };
  const context = { repo: { owner: 'mikidaren', repo: 'Miki-GrasshopperWarehouse' },
    payload: { workflow_run: { ...f.run, pull_requests: [{ number: 1 }] }, repository: { default_branch: 'main' } } };
  await approve({ github, context, core: { info() {} } });
  assert.equal(written.length, 0);
});

test('eligible pull request is approved, squash-merged and its same-repository branch deleted', async () => {
  const f = fixture(); f.pr.number = 1; f.pr.title = 'Add an example';
  f.pr.head = { sha: 'abc', ref: 'codex/add-example', repo: { full_name: f.repository } };
  const reads = [f.pr, f.pr, f.pr];
  const written = [];
  const merges = [];
  const deletions = [];
  const listFiles = async () => {};
  const github = {
    paginate: async method => method === listFiles ? f.files : [],
    rest: { pulls: {
      get: async () => ({ data: reads.shift() }), listFiles, listReviews: async () => {},
      createReview: async review => written.push(review),
      merge: async request => { merges.push(request); return { data: { merged: true } }; }
    }, git: { deleteRef: async request => deletions.push(request) } }
  };
  const context = { repo: { owner: 'mikidaren', repo: 'Miki-GrasshopperWarehouse' },
    payload: { workflow_run: { ...f.run, pull_requests: [{ number: 1 }] }, repository: { default_branch: 'main' } } };
  await approve({ github, context, core: { info() {} } });
  assert.equal(written.length, 1);
  assert.deepEqual(merges[0], { owner: 'mikidaren', repo: 'Miki-GrasshopperWarehouse', pull_number: 1,
    sha: 'abc', merge_method: 'squash', commit_title: 'Warehouse: Add an example (#1)' });
  assert.deepEqual(deletions[0], { owner: 'mikidaren', repo: 'Miki-GrasshopperWarehouse',
    ref: 'heads/codex/add-example' });
});

test('stale head before merge does not merge, delete or write another review', async () => {
  const f = fixture(); f.pr.number = 1;
  f.pr.head = { sha: 'abc', ref: 'codex/add-example', repo: { full_name: f.repository } };
  const reads = [f.pr, f.pr, { ...f.pr, head: { ...f.pr.head, sha: 'new' } }];
  const written = [];
  const merges = [];
  const deletions = [];
  const listFiles = async () => {};
  const github = {
    paginate: async method => method === listFiles ? f.files : [],
    rest: { pulls: {
      get: async () => ({ data: reads.shift() }), listFiles, listReviews: async () => {},
      createReview: async review => written.push(review),
      merge: async request => merges.push(request)
    }, git: { deleteRef: async request => deletions.push(request) } }
  };
  const context = { repo: { owner: 'mikidaren', repo: 'Miki-GrasshopperWarehouse' },
    payload: { workflow_run: { ...f.run, pull_requests: [{ number: 1 }] }, repository: { default_branch: 'main' } } };
  await approve({ github, context, core: { info() {} } });
  assert.equal(written.length, 1);
  assert.equal(merges.length, 0);
  assert.equal(deletions.length, 0);
});
