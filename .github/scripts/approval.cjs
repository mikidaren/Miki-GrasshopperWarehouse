function eligibility({ repository, defaultBranch, run, pr, files }) {
  const manual = reason => ({ eligible: false, reason });
  if (run.conclusion !== 'success' || run.event !== 'pull_request' || run.path !== '.github/workflows/validate.yml')
    return manual('A successful pull-request validation run is required.');
  if (pr.state !== 'open' || pr.draft || pr.base.ref !== defaultBranch)
    return manual('Only open, ready PRs to the default branch are eligible.');
  if (pr.head.sha !== run.head_sha) return manual('Validation belongs to a stale head.');
  if (pr.head.repo?.full_name !== repository || !['OWNER', 'MEMBER', 'COLLABORATOR'].includes(pr.author_association) || pr.user.type !== 'User')
    return manual('Contributor and same-repository trust checks require maintainer review.');
  const allowed = path => /^examples\/iceworld\/[a-z0-9]+(?:-[a-z0-9]+)*\.json$/.test(path)
    || ['examples/index.json', 'docs/warehouse-index.md', 'examples/iceworld/README.md'].includes(path);
  if (!files.length || files.length > 100) return manual('Change set must contain 1–100 files.');
  if (files.some(file => !['added', 'modified'].includes(file.status) || !allowed(file.filename)))
    return manual('Change set includes files requiring maintainer review.');
  return { eligible: true, reason: 'Trusted numeric examples or index documentation passed validation at the current head.' };
}

async function approve({ github, context, core }) {
  const run = context.payload.workflow_run;
  const { owner, repo } = context.repo;
  const repository = `${owner}/${repo}`;
  const defaultBranch = context.payload.repository.default_branch;
  for (const linked of run.pull_requests ?? []) {
    const { data: pr } = await github.rest.pulls.get({ owner, repo, pull_number: linked.number });
    const files = await github.paginate(github.rest.pulls.listFiles, { owner, repo, pull_number: pr.number, per_page: 100 });
    const decision = eligibility({ repository, defaultBranch, run, pr, files });
    core.info(`PR #${pr.number}: ${decision.reason}`);
    if (!decision.eligible) continue;
    const reviews = await github.paginate(github.rest.pulls.listReviews, { owner, repo, pull_number: pr.number, per_page: 100 });
    if (reviews.some(review => review.user?.login === 'github-actions[bot]' && review.state === 'APPROVED' && review.commit_id === pr.head.sha)) continue;
    // Recheck immediately before writing a review, then bind the review to the validated commit.
    const { data: current } = await github.rest.pulls.get({ owner, repo, pull_number: pr.number });
    if (!eligibility({ repository, defaultBranch, run, pr: current, files }).eligible) continue;
    await github.rest.pulls.createReview({ owner, repo, pull_number: pr.number, commit_id: pr.head.sha, event: 'APPROVE',
      body: 'Automatic warehouse intake review passed for numeric examples and index documentation. Validation covers the referenced commit. Native geometry validation remains separately recorded in the example index.' });
  }
}
module.exports = { eligibility, approve };
