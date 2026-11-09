# Miki-GrasshopperWarehouse

Reusable lightweight OpenBIM kernels for Rhino 9, Grasshopper and the Miki-Rhino / Magpie-Rhino workspace.

This library targets concept and developed architectural design. Supply plan positions, storey heights, wall thickness and simple door/window forms. Materials, wall build-ups, reinforcement and construction-junction specifications are deliberately not required.

## What is here

- `src/Miki.OpenBim`: portable project contract, validation, small ID-based patches and dependency calculation.
- `src/Miki.OpenBim.Rhino`: real RhinoCommon curves, closed Breps, wall voids, slabs, conceptual stairs and shared Block definitions.
- `python/miki_ifc.py`: optional IFC4 exchange and actual geometry readback using IfcOpenShell.
- `examples/two-storey.json`: a generic 12 × 8 m two-storey test building, not a proposed design for a real project.
- `tests`: portable contract and IFC tests. Native geometry additionally requires Rhino 9.
- `examples/iceworld`: six slider-driven Grasshopper `.ghx` examples for stairs, handrails, queue barriers, curved ice blocks and spiral ornament.
- `src/Miki.IceWorld`, `src/Miki.IceWorld.Rhino` and `src/Miki.IceWorld.Grasshopper`: bounded numeric geometry, a stable Rhino adapter for Miki plugins, and policy-enforced Grasshopper components.
- `examples/index.json`, `docs/warehouse-index.md` and `docs/approval-rules.md`: classified examples and separate GitHub/host approval rules.
- `policies/iceworld-standard-baseline.json` and `docs/iceworld-standard-baseline.md`: Chinese Standard design baselines, with project-specific fire, structural, accessibility and ice-durability review kept explicit.

The architectural toolkit is a reusable compiled parametric kernel callable from a Rhino plugin or a Grasshopper C# component. The IceWorld collection also supplies visual `.ghx` documents with numeric sliders and grouped inputs/outputs. Chinese setup and example instructions are in [examples/iceworld/README.md](examples/iceworld/README.md).

Miki `.rhp` integration is available through `Miki.IceWorld.Rhino.dll` and `RhinoModel.BuildIceWorld` / `BakeIceWorld`. The adapter resolves examples by stable ID, verifies catalog hashes and applies the standard-candidate bake gate. Integration details and installation layout are in [docs/plugin-integration.md](docs/plugin-integration.md).

## Minimal edits

Lengths are metres. A type edit is a compact patch, not a new modeling script:

```json
{"types":[{"id":"WIN-A","width":1.5}]}
```

The engine finds every instance of that type and its host wall. The native adapter updates the shared Block and real wall openings. Different types retain their geometry. A level edit follows the same rule:

```json
{"levels":[{"id":"L2","height":3.5}]}
```

Changing one level's height does not implicitly move another level's explicitly assigned elevation. Specify that elevation in the same patch when that is the intended design change.

## Validation

```text
dotnet run --project tests/Domain.Tests --configuration Release
python -m pip install -r python/requirements.txt
python -m unittest discover -s tests -p "test_ifc*.py" -v
python python/miki_ifc.py export --project examples/two-storey.json --output two-storey.ifc --report two-storey.validation.json
```

Use an isolated Python 3.12 environment. The Rhino adapter builds against RhinoCommon 9; it must execute inside Rhino, not a normal .NET process.

IFC export is not verified by the presence of a file alone. The worker reopens the IFC, checks storeys/types/void and filling relationships and creates actual OpenCASCADE shapes for geometric comparisons. Source JSON and native `.3dm` remain the editable parametric source; IFC exchange does not promise a Revit family or a Grasshopper history round trip.

## Native API

```text
RhinoModel.ApplyJson(document, projectJson)
RhinoModel.PatchJson(document, patchJson)
RhinoModel.InspectJson(document)
RhinoModel.ReadProjectJson(document)
RhinoModel.BuildIceWorld(document, warehouseRoot, exampleId, context)
RhinoModel.BakeIceWorld(document, warehouseRoot, exampleId, context)
```

The caller owns the undo record and UI-thread scheduling. The adapter stores source on an undoable hidden metadata object and mirrors it in document strings. It refuses to overwrite manually edited or missing managed geometry. Unrelated objects are not adopted or modified.

## Boundaries

- Straight walls, rectangular door/window types and straight conceptual stairs are the first supported tool family.
- Shared geometry is implemented with actual Rhino Block definitions and instances.
- No automatic code-compliance, structural analysis, fabrication or construction-detail claims.
- Chinese Standard baselines configure concept and developed-design parameters. Passing a parameter check does not replace project-specific fire, structural, accessibility or GB 51202-2016 review.
- Standard-candidate examples are blocked from automatic Bake when their declared stair or guard parameters fail the configured baseline. Existing concept examples remain visual concept tests and their hashes are unchanged.
- No model-provider account keys, network calls or code downloads in the kernels.
- Product integration consumes a pinned local source snapshot. Updating the library is an explicit developer action.
