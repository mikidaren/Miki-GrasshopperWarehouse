# Validation scope

Validated locally on 2026-11-09–10 with Rhino 9 on Windows, .NET 8 targets, Python 3.12 and IfcOpenShell 0.9.0.post1.

## Portable checks

- 210 domain assertions: minimal contract, omitted wall height and type depth, geometry references, simple polygons, stair void/landing relationships, nonfinite/invalid inputs, partial patches and affected IDs.
- 43 IFC worker tests: real IFC4/OpenCASCADE export/readback, mapped types, actual wall voids, spatial containment, stable identities, changed dimensions, corrupt files, output conflicts, atomic publication and killed-worker lock recovery.
- The 5,000 generated-part estimate is checked before geometry expansion. The two-storey example estimates 87 parts.

## Native checks

The generic example has two storeys, ten walls including two interior partitions, nine openings, three shared types and one straight stair. It contains real closed Breps, exact plan curves and actual Rhino Block instances.

- 23 native model assertions: create, four shared-window updates, stable instance identities, untouched different types, scoped second-storey height changes and no Mesh/SubD document output.
- 10 native regressions: preserve manual layer/name/colour/group/custom data, including nested layers with the same leaf name; refuse manual geometry overwrite and unit drift; accept plan-first projects without walls; distinguish case-sensitive type IDs; build metre-based source in a millimetre document.
- All three `.3dm` states reopen and pass native geometry/source inspection.
- Three revisions export and reopen as IFC4. Independent native/IFC comparisons cover 66 solid-product volumes and bounds, with stable IFC IDs.

The host product additionally exercises approvals, cancellation, failure resolution, streaming and source-snapshot checks. Those tests live in the product repository rather than this warehouse.

An actual request in Rhino's native AI host called the local inspection tool. A second request patched only `L2.height` and read back revision 3, without transmitting a modeling script. Native `_Undo` then restored geometry and authoritative source to revision 2. The integrated product's IFC export service also completed a verified export.

## Reproducing the native scripts

Build `src/Miki.OpenBim.Rhino`, then start an owned Rhino process with these environment variables already set:

- `MIKI_OPENBIM_BINARY_DIRECTORY`: directory containing the two compiled Miki assemblies.
- `MIKI_OPENBIM_QA_OUTPUT`: dedicated artifact directory.
- `MIKI_OPENBIM_QA_PID_FILE`: file containing that owned process's PID.

Run `tests/native/verify-two-storey.csx` in its empty document, then `regressions.csx` and `reopen-and-capture.csx`. The scripts are intended for Rhino's C# script runner/MCP wrapper. They are not commands to run against an existing design document.

## Limits

This is a bounded architectural concept/developed-design pilot. There is no structural/code-compliance validation, construction-detail guarantee, curved-wall/arched-window coverage or claim of full Revit parity. IFC readback uses IfcOpenShell, not a verified commercial Revit import. IFC is not the full native parametric source. Generated internal render meshes do not turn native Brep document objects into mesh deliverables.

Overall token savings have not been measured. Repeated edits use compact parameter patches and locally installed kernels; source scripts are not sent back through the model for each edit.
