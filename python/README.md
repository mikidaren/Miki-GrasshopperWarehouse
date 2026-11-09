# IFC4 export and geometric readback worker

Use an explicitly configured Python 3.12 environment containing `ifcopenshell==0.9.0.post1`. The exact dependency is pinned in `requirements.txt`. The worker checks the installed version when invoked. Runtime configuration belongs to the calling application.

## CLI

Run these commands from the warehouse directory with the configured Python executable.

```text
python python/miki_ifc.py export --project SOURCE.json --output MODEL.ifc --report REPORT.json
python python/miki_ifc.py inspect --project SOURCE.json --input MODEL.ifc --report REPORT.json
```

`export` requires both destination paths. Existing model and report files each require explicit `--overwrite`. `inspect` applies the same rule to its report destination. Destination parents must already exist. Source, model and report paths must identify distinct files, including hard-link and symbolic-link aliases.

Export stages both files beside their destinations, reopens the staged IFC and completes verification before publication. Exclusive OS advisory locks serialize cooperating writers. Windows uses `msvcrt.locking` on byte zero and Unix uses `fcntl.flock`. Open file descriptors retain ownership for the entire operation. Closing the descriptors or terminating the worker releases ownership automatically.

Empty `.miki-ifc.lock` sidecar files persist after success, failure and termination. Their stable filesystem identities let all writers contend on the same lock. The worker leaves these marker files in place, including during failed lock acquisition. Existing empty markers from earlier worker versions are reusable. OS lock state determines whether a target is busy. Default publication uses atomic create-if-absent links. Explicit overwrite backs up existing files and rolls back completed replacements if publication fails. Caught failures clean staging files.

The report file contains the full compact result. With `--report`, stdout contains only verification status, project identity, revision, counts and failed checks. `inspect` can omit `--report` to return the full report on stdout.

| Exit | Meaning |
| --- | --- |
| `0` | All verification checks pass and requested outputs are published |
| `1` | Readback verification fails |
| `2` | Invalid input, unavailable runtime, output conflict or file operation failure |

## Python interface

The single module is importable by file path, independently of the repository root.

- `validate_project(source)` accepts a source dictionary or JSON file path and returns a validated copy with default slab `offset` and `voids` values and type `depth` resolved to `.12` metres when omitted.
- `block_specs(type)` accepts a validated type dictionary and returns five local boxes in metres. Each box has `min` and `max` arrays with three coordinates.
- `export_project(source, output_path, report_path, overwrite=False)` returns the full verified report after publication. `overwrite` is keyword-only.
- `inspect_project(source, input_path)` reopens an IFC file and returns the full report, including failed checks for geometric or semantic mismatches.
- `WorkerError` exposes `code` and an optional failed verification `report`. CLI errors are structured JSON with `verified` and `passed` set to `false`.

Source schema version 1 uses the approved `levels`, `grids`, `spaces`, `walls`, `slabs`, `types`, `openings` and `stairs` arrays. IDs are unique across the project. The input limit is 10 MiB with at most 10,000 collection entries. Validation checks finite bounded numbers, positive dimensions, reference resolution, simple polygons, slab void containment, opening fit and overlap. Stair validation requires supported source and target landings, a target slab void covering the flight, and clearance through intersected slabs. Flights support 2 through 1,000 risers with a positive tread and a riser height at most one metre.

The domain engine's 5,000 generated-part budget also applies to direct worker calls. It runs before detailed polygon, stair or IFC geometry processing. The estimate is `grids + spaces + 2*walls + 2*slabs + 2*openings + 5*types + sum(stair.risers+1) + 1`, where collection names denote their entry counts. The final unit accounts for project state. Level metadata has zero generated-part cost. Exactly 5,000 parts is accepted. An over-budget source returns structured `invalid_project` JSON with the estimate and limit before creating output files or lock markers.

Grids are validated source references and are counted in `sourceCounts`. Grid drawing content stays in the source and native plan layer. The IFC product inventory comprises the building, storeys, spaces, walls, slabs, stair flights, openings and fills.

## Shared geometric conventions

Wall local x follows the normalized baseline. Local y is the left normal `[-direction.y, direction.x]`. The baseline is the wall centreline. Opening offset locates its left edge and sill is relative to the storey elevation. Openings cut the full host thickness and use `IfcRelVoidsElement`. Door and window fills use `IfcRelFillsElement`.

`block_specs(type)` orders its boxes as bottom rail, top rail, left jamb, right jamb and centre pane or panel. For type width `w`, height `h` and depth `d`, define `f = min(.06, w/4, h/4)`.

| Box | Minimum | Maximum |
| --- | --- | --- |
| Bottom rail | `[0, -d/2, 0]` | `[w, d/2, f]` |
| Top rail | `[0, -d/2, h-f]` | `[w, d/2, h]` |
| Left jamb | `[0, -d/2, f]` | `[f, d/2, h-f]` |
| Right jamb | `[w-f, -d/2, f]` | `[w, d/2, h-f]` |
| Centre | `[f, -c/2, f]` | `[w-f, c/2, h-f]` |

Window centre depth `c = min(.012, d)`. Door centre depth `c = min(.04, d)`. All four frame bars use the full depth. The boxes touch along boundaries and have disjoint interiors. Their total volume is `(w*h - innerArea)*d + innerArea*c`, where `innerArea = (w-2*f)*(h-2*f)`. Every occurrence of one type references its single `IfcRepresentationMap` through `IfcMappedItem`. One `IfcRelDefinesByType` groups all occurrences of that type.

Slab top is `level.elevation + offset` and bottom is `top - thickness`. Slab holes use `IfcArbitraryProfileDefWithVoids`.

A stair origin is the left-front point. Its normalized direction supplies local x, with width extending along the left normal. For `N` treads, tread `i` covers local x from `i*run/N` through `(i+1)*run/N`, starting at the source elevation and reaching `(i+1)*rise/N`. A single exact swept stepped profile represents this union as `IfcStairFlight`. Expected volume is `width*run*rise*(N+1)/(2*N)`. Plan bounds come from the rotated flight rectangle and vertical bounds from the two level elevations.

## Identity and report contract

Project namespace is `UUID5(NAMESPACE_URL, project.id)`. A source entity uses `UUID5(projectNamespace, semanticId)`, compressed as an IFC GlobalId. Synthetic building and fill IDs are `$building` and `$fill/OPENING_ID`. Revision changes preserve these identities. `Pset_MikiOpenBim` carries `SourceId`, `ProjectId`, `Role` and the project's `Revision`.

The plugin acceptance field is root `verified`. It becomes `true` only when every check succeeds. Root `passed` is an equivalent compatibility field. Other root fields include `schemaVersion`, `projectId`, `revision`, `ifcSchema`, `engine`, `sourceCounts`, `counts`, `checks`, `products`, `quantities` and `failedChecks`.

`checks` contains named boolean results for IFC4 schema, attribute and inverse constraints, unique identities, quantities, complete product inventory, metre units, exact representation classes, revision, source properties, storeys, shared mappings, void/fill links and actual geometry.

Each `products` entry identifies its source `id`, `role`, `ifcClass`, stable `globalId` and `level`. Openings and fills also carry `hostId` and `typeId`. Geometry entries contain actual `volume` in cubic metres, `expectedVolume`, world `bounds.min` and `bounds.max` in metres, and geometric `passed`. Walls include measured `grossVolume` and `voidVolume`. `quantities` sums actual measured volumes by product role.

Readback forces the bundled OpenCASCADE kernel through `ifcopenshell.geom`. Native geometry supplies volume and closed-manifold checks. World-space conversion supplies bounds. Gross wall geometry is separately read with opening subtraction disabled and compared to net geometry. Expected measurements are derived from the source dimensions and polygons. Volume comparison uses relative tolerance `1e-6` and absolute tolerance `1e-7` cubic metres. Bounds use absolute tolerance `1e-6` metres.

## Tests and evidence

Run the complete worker suite with the configured runtime.

```text
python -B -m unittest discover -s tests -p "test_ifc*.py" -v
```

The complete suite has 43 tests, including the updated canonical example with 10 walls and 9 openings, type width `1.2 -> 1.5`, only `L2.height` changing `3.2 -> 3.5`, rotated geometry, capped frame dimensions, a 1,000-riser sweep, corrupt IFC relations/geometry, invalid source data, output conflicts, concurrent writers and publication rollback. Windows crash tests terminate only the worker's own PID, accounting for the venv executable's launcher process. Development RED/GREEN logs remain local because tracebacks contain machine paths; the published tests reproduce the checks. See `docs/validation.md` for the verification scope.
