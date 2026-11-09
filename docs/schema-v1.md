# Portable architectural project schema v1

The dependency-free `net8.0` assembly exposes namespace `Miki.OpenBim`. Source lengths are metres. The public records and their fields are defined in `src/Miki.OpenBim/BuildingProject.cs`.

## Public engine

```csharp
public const long MaxGeneratedParts = 5000;
public static JsonSerializerOptions JsonOptions { get; }
public static BuildingProject Parse(string json);
public static string Serialize(BuildingProject project);
public static void Validate(BuildingProject project);
public static long EstimateGeneratedParts(BuildingProject project);
public static BuildingProject CreateDemo();
public static BuildingProject ApplyPatch(BuildingProject project, JsonObject patch);
public static string[] AffectedIds(BuildingProject before, BuildingProject after);
```

`JsonOptions` is shared and read-only. JSON uses case-sensitive camelCase properties and strict numeric values. Unknown or duplicate properties, comments, trailing commas and missing required fields are rejected. `Parse` wraps JSON errors in `InvalidOperationException` and retains the inner `JsonException`. All engine validation failures use `InvalidOperationException`.

`BuildingProject` carries `SchemaVersion`, `Id`, `Name`, `Revision`, `Units`, and arrays `Levels`, `Grids`, `Spaces`, `Walls`, `Slabs`, `Types`, `Openings`, `Stairs`. Schema version is 1 and units are `m`. Each collection is required and may be empty except `Levels`, which must contain at least one level. Entries and collections must be non-null. IDs are ordinal, case-sensitive and globally unique, including the project ID.

The public child records are `BimLevel`, `BimGrid`, `BimSpace`, `BimWall`, `BimSlab`, `BimType`, `BimOpening`, `BimStair`. XY values use `double[]`, polygon rings use `double[][]`, and slab `Voids` uses `double[][][]`. A wall's nullable `Height` defaults to null and follows its referenced level. Slab `Offset` defaults to zero and `Voids` defaults to an empty array. `BimSlab` exposes `Voids` as an init property alongside its positional constructor `(Id, Level, Boundary, Thickness, Offset = 0)`.

Window and door type `Depth` is the last constructor parameter and defaults to 0.12 m when omitted. `Form` is required and must be `rectangular`. Explicit depth values remain subject to positive finite length validation. An omitted depth on a new type resolves to 0.12 m, while an omitted depth in a partial existing-type patch preserves that type's current value. `Serialize` writes the resolved numeric depth explicitly for downstream IFC export. The matching direct worker CLI input contract also requires the 0.12 m default.

## Bounds and geometric conventions

The following deterministic limits keep concept-level input finite and bounded.

| Parameter | Accepted range |
| --- | --- |
| ID | 1–128 ASCII letters, digits, `.`, `_`, `-`, starting with a letter or digit |
| Project or space name | 1–256 characters, with at least one non-whitespace character |
| Revision | 0 through `int.MaxValue`, with overflow rejected on a nonempty patch |
| Total child records | At most 10,000 |
| Expanded generated parts | At most 5,000, including native and plan objects, shared definition solids and project state |
| XY coordinate or level elevation | Absolute value at most 1,000,000 m |
| Positive length or baseline length | Greater than 1e-8 m and at most 10,000 m |
| Slab offset | Absolute value at most 10,000 m |
| Polygon | 3–1,024 vertices and nonzero area greater than 5e-9 m² |
| Slab voids | At most 64 mutually disjoint polygons |
| Stair risers | 2–1,000, with positive rise per riser at most 1 m |
| Input JSON | At most 16 Mi characters and nesting depth 64 |

This bounded pilot targets small architectural concept-design projects with a 5,000-object expansion budget. The shared public estimator returns a `long` using the following formula.

```text
grids + spaces + 2 * walls + 2 * slabs + 2 * openings
    + sum(stair.risers + 1) + 5 * types + 1
```

Collection names in the formula denote their entry counts. Each staircase contributes its risers and one landing. Each shared type contributes five definition solids, and one slot covers project state. Level metadata adds zero generated parts. Every product and sum uses 64-bit integer arithmetic, including promotion before adding one to a riser count.

`Validate` checks collection structure, IDs and the riser count range before applying this budget, ahead of polygon and per-tread geometry processing. Accepted projects estimate at most 5,000 generated parts, with exact equality allowed. Oversize projects fail with `InvalidOperationException` reporting both estimate and limit. `Parse`, `Serialize` and `ApplyPatch` pass through this validation, so an oversize patch fails atomically. The estimator can report values above the budget for inspection. It performs counting without native geometry allocation, and `Validate` remains the project acceptance entry point. The current two-storey demo estimates 87 generated parts.

Plan-first input may contain `walls: []`, with spaces, grids and other independently valid collections. Semantic IDs retain ordinal case-sensitive identity, so IDs such as `WIN-A` and `win-a` can coexist.

Every numeric value must be finite. Polygon rings close implicitly, so the first vertex must not be repeated. Either winding is accepted. Self-crossings, self-touching and backtracking edges are rejected. Slab voids must lie strictly inside the boundary and cannot touch or overlap each other. A slab's top is `level.Elevation + slab.Offset`, and its bottom is one thickness lower.

Openings use a left-edge distance along the directed wall baseline and a sill height above the wall base. Their referenced type supplies width, height and depth. Openings must fit their wall length and effective height. Two openings may share a horizontal interval if their vertical interiors are disjoint. Boundary-only contact is accepted.

A stair's `Origin` is the lower-left front corner. `Direction` is normalized along the run. Width extends to its left normal `[-directionY, directionX]`. The target elevation must exceed the source elevation. A lower landing extends backward by one stair width, and an upper landing extends forward from the end by one width. Both require slab support at the respective level elevation. A target-level slab void must cover the flight footprint, while its upper landing stays on slab surface. Positive-volume intersections between conceptual solid treads and any slab, including intermediate storeys and offsets, are rejected. Boundary-only contact is accepted.

## Patch semantics

Patch roots accept `name` and the eight collection names. Identity, units, schema version and revision are engine-controlled. Every collection entry requires an ID. Existing entries merge supplied fields, and complete new entries append in input order. Partial entries can refer to other entries added in the same patch. Nested geometry arrays replace their field value in full. Root collections are merged, so an empty array preserves their existing entries.

An empty object preserves the revision. Every accepted nonempty patch increases it once, including an unchanged field or an empty merge array. Missing fields preserve their current value. A null wall height restores level inheritance. Unsupported properties and deletion requests fail. Inputs and patches remain untouched, and the returned project owns fresh arrays. A rejected patch leaves its source unchanged.

## Scoped source IDs

`AffectedIds` compares arrays structurally and returns a distinct ordinal-sorted list of source element IDs. Root name or revision changes have no geometry IDs. Root project identities must match. Collection reordering alone has no geometry effect.

Changed types include every matching opening and its old or new host wall. Changed openings include their old and new host walls. Direct wall changes include its opening instances. Level changes include dependent walls, openings, spaces, slabs and connecting stairs. Unrelated objects stay outside the returned set. A host wall included through a type or opening change does not cause unrelated openings on that wall to enter the set.

Native adapters can match these IDs to both native entities and plan roots through their `sourceId`. Native GUIDs are adapter-owned.

## Two-storey example

`examples/two-storey.json` round-trips to `CreateDemo()`. Its initial revision is deliberately zero. The plan spans 12 × 8 m with six grid axes, four named room zones, ten 0.24 m walls, two 0.2 m slabs, nine openings, three reusable types and one straight stair. Levels L1 and L2 have elevations 0 and 3.2 m and heights 3.2 m. Four windows share `WIN-A`, with width 1.2 m, height 1.4 m and depth 0.12 m. `WIN-B` supplies two additional windows and `DOOR-A` supplies the ground-floor entrance and both partition doors. Wall heights follow their levels.

Each storey has a straight interior partition from `[0, 4]` to `[12, 4]`. Its door opening starts at offset 8.5 m and sill zero. North room boundaries begin at y = 4.12 m, on the north face of the 0.24 m partition.

The stair has 18 risers, run 4.8 m, width 1.1 m and origin `[3.2, 2.5]`. The upper void ends at x = 8 m, followed by a supported 1.1 m landing. No roof slab is included.

Changing `WIN-A.width` to 1.5 m returns exactly these source IDs.

```text
WALL-L1-S
WALL-L2-S
WIN-A
WIN-L1-1
WIN-L1-2
WIN-L2-1
WIN-L2-2
```

The domain tests exercise these edits and the subsequent `L2.height` change to 3.5 m. Reproduce them with the command in `tests/Domain.Tests/verification.md`.
