# IceWorld warehouse design

The requested scope comprises six reusable Grasshopper examples, a classified index, GitHub intake approval and Rhino/Grasshopper execution approval. The user confirmed that both approval systems are required.

## Approach

Extend the warehouse with a separate portable geometry kernel and a Grasshopper assembly. Keep the existing architectural project contract intact. Use compiled components with numeric inputs and distributable `.ghx` documents. This is the implementation choice made for this delivery. Embedded scripting would duplicate validation across files. Extending the existing BIM schema for ornaments and queue rails would mix unrelated contracts.

## Deliverables

- Straight stair and two handrails, switchback stair with platform, platform perimeter railing, queue barriers, segmented curved ice wall, spiral surface ornament.
- Stable example IDs, one primary category, searchable tags, explicit metre units and parameter ranges, dependencies, output groups and separate validation statuses.
- GH canvas groups for inputs, generation and outputs. Native objects use per-example groups and role layers.
- GitHub automatic approval of same-repository trusted-contributor documentation and numeric example JSON changes after successful validation at the current PR head. Kernel, workflow, policy, GH document and binary changes require maintainer review.
- Host execution decisions for inspect, preview and bounded new-object baking. The bake entry point enforces the policy before document mutation. Existing example objects, unknown operations, writes and deletion require explicit host handling.

## Constraints

All source lengths use metres. Example dimensions are test assumptions. Project dimensions and capacity remain undecided. The models are concept geometry. Code compliance, structural capacity and fabrication are outside the validation performed here. Reject unknown/nonfinite/out-of-range parameters and limit generated parts to 5000. Baking scales to document units, validates geometry first and rolls back newly created objects on failure. GH execution does not bake automatically during recompute. A button edge requests baking.

## Validation

Portable tests cover all six generators, counts and hand-derived coordinates, invalid input and execution decisions. Native QA constructs actual Rhino Breps, checks validity and closure, creates/reopens the six GH documents and evaluates them in an owned headless Rhino process. Record unavailable checks explicitly. Existing domain and IFC tests continue to run.
