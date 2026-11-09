# Domain verification

The suite is a dependency-free .NET 8 executable. Each named case catches its own failure and contributes to the final count. A failure exits with code 1.

## Initial RED

Command run from the workspace on 2026-11-09

```powershell
dotnet run --project outputs/Miki-GrasshopperWarehouse/tests/Domain.Tests/Domain.Tests.csproj --configuration Release
```

Observed exit code 1, with a successful build and the following summary.

```text
FAIL validate independent fixture [NotImplementedException] The method or operation is not implemented.
FAIL strict camelCase JSON round trip [NotImplementedException] The method or operation is not implemented.
PASS shared options reject unknown members
...
FAIL example JSON matches demo and validates [FileNotFoundException] Could not find file .../two-storey.json
RESULT 1/163 passed, 162 failed
```

The engine had public contract stubs at this point. The example fixture had yet to be created. No implementation preceded this run.

## First GREEN

The same command returned exit code 0 after implementation.

```text
RESULT 163/163 passed, 0 failed
```

## Stair regression RED

Seven further cases exercised geometric boundary contact, offset and intermediate floors, strict JSON, and rigid transformations. The same command returned exit code 1.

```text
FAIL landing slab may touch the flight end without obstructing it [InvalidOperationException] ST flight intersects upper slab material.
FAIL intermediate storey slab cannot intersect stair flight [Exception] Expected InvalidOperationException
FAIL offset slab cannot intersect stair flight [Exception] Expected InvalidOperationException
RESULT 167/170 passed, 3 failed
```

The root causes were an inclusive 2D contact test and consideration of only zero-offset target-storey slabs. The correction checks positive-area intersections of each conceptual solid tread with slab regions at intersecting elevations. Hole areas are subtracted and boundary-only contact is accepted.

## Final GREEN

The full Release suite returned exit code 0 after the regression correction.

```text
PASS landing slab may touch the flight end without obstructing it
PASS intermediate storey slab cannot intersect stair flight
PASS offset slab cannot intersect stair flight
PASS rotated translated project remains valid
PASS demo contract and shared types
PASS demo survives acceptance patches with unrelated type unchanged
PASS example JSON matches demo and validates
RESULT 170/170 passed, 0 failed
```

The library was built independently using the following command, which returned exit code 0, zero warnings and zero errors.

```powershell
dotnet build outputs/Miki-GrasshopperWarehouse/src/Miki.OpenBim/Miki.OpenBim.csproj --configuration Release --no-restore
```

## Interior partition RED

The requested demo refinement added ten cases before editing the demo or its JSON example. The full Release command returned exit code 1. The existing 170 cases passed, as did the new revision-zero and stair-preservation cases. The eight new partition, door, room-boundary and dependency checks failed as expected.

```text
FAIL demo L1 has a full-width interior partition [Exception] Expected a level-height partition from [0,4] to [12,4]
FAIL demo L1 partition has a shared door opening [Exception] Expected one DOOR-A opening at offset 8.5 and sill 0
FAIL demo L1 north zone starts beyond the partition centreline [Exception] Expected north boundary y=4.12 at the partition face
FAIL demo L2 has a full-width interior partition [Exception] Expected a level-height partition from [0,4] to [12,4]
FAIL demo L2 partition has a shared door opening [Exception] Expected one DOOR-A opening at offset 8.5 and sill 0
FAIL demo L2 north zone starts beyond the partition centreline [Exception] Expected north boundary y=4.12 at the partition face
FAIL demo shared door edit affects both partitions and exterior door [Exception] Door type updates must include exactly its instances and hosts
FAIL demo upper storey height affects its partition and door [Exception] Partition and door dependencies must stay storey-scoped
RESULT 172/180 passed, 8 failed
```

## Interior partition GREEN

After adding the two partitions and shared door openings, shifting both north boundaries to y = 4.12 m, and synchronizing the JSON example, the same Release command returned exit code 0.

```text
PASS demo L1 has a full-width interior partition
PASS demo L1 partition has a shared door opening
PASS demo L1 north zone starts beyond the partition centreline
PASS demo L2 has a full-width interior partition
PASS demo L2 partition has a shared door opening
PASS demo L2 north zone starts beyond the partition centreline
PASS demo deliberately starts at revision zero
PASS demo partition preserves stair void and landing geometry
PASS demo shared door edit affects both partitions and exterior door
PASS demo upper storey height affects its partition and door
PASS demo survives acceptance patches with unrelated type unchanged
PASS example JSON matches demo and validates
RESULT 180/180 passed, 0 failed
```

The demo now has ten walls and nine openings. The new source IDs are `WALL-L1-P`, `WALL-L2-P`, `DOOR-L1-P`, and `DOOR-L2-P`. Initial revision zero is intentional, an empty patch preserves it, and the first accepted nonempty patch yields revision one.

## Expanded geometry budget RED

The P1 regression added 21 cases before budget enforcement. `EstimateGeneratedParts` had only its public signature stub for the initial run. The same full Release command returned exit code 1. All 180 existing cases passed and the new case-sensitive semantic-ID regression also passed.

```text
FAIL generated budget rejects 6000 otherwise valid stair copies [Exception] Expected InvalidOperationException
FAIL generated budget rejects five thousand-riser flights [Exception] Expected InvalidOperationException
FAIL generated budget rejects 5001 parts [Exception] Expected InvalidOperationException
FAIL generated budget runs before detailed polygon and stair geometry [Exception] Expanded budget must fail before polygon processing and report estimate and limit
FAIL generated budget protects parse serialize and atomic patches [Exception] Expected InvalidOperationException
...
PASS case-sensitive semantic IDs remain distinct through parsing and patches
RESULT 181/201 passed, 20 failed
```

The first case used 6,000 valid four-riser flights with unique source IDs, which the previous validator accepted. A separate five-flight case used 1,000 risers each and also passed the previous validator. The count estimator cases exercise all formula weights, state, exact boundary, long arithmetic, the 9,009,040-part report, and valid wall-free plan input.

## Expanded geometry budget GREEN

After enforcing the shared 64-bit estimate ahead of detailed geometry validation, the full Release command returned exit code 0.

```text
PASS generated budget rejects 6000 otherwise valid stair copies
PASS generated budget rejects five thousand-riser flights
PASS generated estimate uses long for individual risers and their sum
PASS generated estimate covers nine million expanded stair parts
PASS generated budget accepts exactly 5000 parts
PASS generated budget rejects 5001 parts
PASS generated budget runs before detailed polygon and stair geometry
PASS generated budget protects parse serialize and atomic patches
PASS plan-first projects support walls empty with spaces and axes
PASS case-sensitive semantic IDs remain distinct through parsing and patches
RESULT 201/201 passed, 0 failed
```

The independent Release library build also returned exit code 0, with zero warnings and zero errors. `ProjectEngine.MaxGeneratedParts` is the public 5,000-part limit and `ProjectEngine.EstimateGeneratedParts(BuildingProject)` is the shared public `long` calculator. The current demo estimate is 87. This cap defines the bounded architectural concept pilot.

## Optional type depth RED

Nine further cases tested the requested concept-stage depth default before changing `BimType`. The full Release command returned exit code 1. Both window and door parsing and new-type patches failed because `depth` was still required. Required form, explicit-null rejection and preservation of an existing nondefault depth already passed.

```text
FAIL omitted window depth defaults to 0.12 and serializes explicitly [InvalidOperationException] Invalid project JSON: JSON deserialization for type 'Miki.OpenBim.BimType' was missing required properties, including the following: depth
FAIL new window type patch may omit depth [InvalidOperationException] Invalid project JSON: JSON deserialization for type 'Miki.OpenBim.BimType' was missing required properties, including the following: depth
FAIL omitted door depth defaults to 0.12 and serializes explicitly [InvalidOperationException] Invalid project JSON: JSON deserialization for type 'Miki.OpenBim.BimType' was missing required properties, including the following: depth
FAIL new door type patch may omit depth [InvalidOperationException] Invalid project JSON: JSON deserialization for type 'Miki.OpenBim.BimType' was missing required properties, including the following: depth
RESULT 206/210 passed, 4 failed
```

## Optional type depth GREEN

After removing `JsonRequired` from the final `BimType.Depth` parameter and assigning its default to 0.12 m, the same full Release command returned exit code 0. Constructor parameter order was preserved. All expanded geometry budget cases remained green.

```text
PASS omitted window depth defaults to 0.12 and serializes explicitly
PASS new window type patch may omit depth
PASS window form remains required with optional depth
PASS window explicit null depth is invalid
PASS omitted door depth defaults to 0.12 and serializes explicitly
PASS new door type patch may omit depth
PASS door form remains required with optional depth
PASS door explicit null depth is invalid
PASS partial type patch preserves explicit nondefault depth
RESULT 210/210 passed, 0 failed
```

Resolved serialization explicitly includes depth. Python's direct minimal-JSON CLI must apply the matching default in its own input normalization. The Python worker is owned by the parent task, while this task's edits cover the portable domain, its tests and schema documentation.
