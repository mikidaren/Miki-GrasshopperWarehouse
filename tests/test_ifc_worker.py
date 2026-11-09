import importlib.util
import json
import os
from pathlib import Path
import signal
import subprocess
import sys
import tempfile
import time
import unittest
from unittest import mock
import uuid


WAREHOUSE = Path(__file__).resolve().parents[1]
WORKER = WAREHOUSE / "python" / "miki_ifc.py"


def project_fixture():
    return {
        "schemaVersion": 1,
        "id": "IFC-TEST",
        "name": "Two storeys",
        "revision": 0,
        "units": "m",
        "levels": [
            {"id": "L1", "elevation": 0.0, "height": 3.2},
            {"id": "L2", "elevation": 3.2, "height": 3.2},
        ],
        "grids": [{"id": "A", "start": [0, 0], "end": [8, 0]}],
        "spaces": [
            {"id": "ROOM-1", "level": "L1", "name": "Room", "boundary": [[0, 0], [8, 0], [8, 6], [0, 6]]},
            {"id": "ROOM-2", "level": "L2", "name": "Room", "boundary": [[0, 0], [8, 0], [8, 6], [0, 6]]},
        ],
        "walls": [
            {"id": "W1", "level": "L1", "start": [0, 0], "end": [8, 0], "thickness": 0.2},
            {"id": "W2", "level": "L2", "start": [0, 0], "end": [8, 0], "thickness": 0.2},
            {"id": "W3", "level": "L1", "start": [8, 0], "end": [8, 6], "thickness": 0.3, "height": 2.8},
        ],
        "slabs": [
            {"id": "F1", "level": "L1", "boundary": [[0, 0], [8, 0], [8, 6], [0, 6]], "thickness": 0.25},
            {
                "id": "F2", "level": "L2", "boundary": [[0, 0], [8, 0], [8, 6], [0, 6]],
                "thickness": 0.25, "offset": 0,
                "voids": [[[1, 1], [5, 1], [5, 2.5], [1, 2.5]]],
            },
        ],
        "types": [
            {"id": "WIN-A", "kind": "window", "form": "rectangular", "width": 1.2, "height": 1.4, "depth": 0.15},
            {"id": "DOOR-A", "kind": "door", "form": "rectangular", "width": 0.9, "height": 2.1, "depth": 0.12},
        ],
        "openings": [
            {"id": "O1", "wall": "W1", "type": "WIN-A", "offset": 1, "sill": 0.9},
            {"id": "O2", "wall": "W2", "type": "WIN-A", "offset": 1, "sill": 0.9},
            {"id": "O3", "wall": "W3", "type": "DOOR-A", "offset": 2, "sill": 0},
        ],
        "stairs": [{"id": "S1", "from": "L1", "to": "L2", "origin": [1.5, 1.25], "direction": [7, 0], "width": 1, "run": 3.5, "risers": 16}],
    }


def project_at_generated_limit():
    project = project_fixture()
    project["grids"] = [{"id": f"G-{index}", "start": [0, 0], "end": [8, 0]} for index in range(4954)]
    return project


class IfcWorkerTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="miki-ifc-test-")
        self.addCleanup(self.temp.cleanup)
        self.directory = Path(self.temp.name)
        self.source = self.directory / "project.json"
        self.output = self.directory / "model.ifc"
        self.report = self.directory / "report.json"
        self.project = project_fixture()
        self.save_source()

    def save_source(self):
        self.source.write_text(json.dumps(self.project, allow_nan=True), encoding="utf-8")

    def run_cli(self, command="export", *extra, isolated=False):
        arguments = [sys.executable]
        if isolated:
            arguments.append("-S")
        arguments.extend([str(WORKER), command, "--project", str(self.source)])
        if command == "export":
            arguments.extend(["--output", str(self.output)])
        else:
            arguments.extend(["--input", str(self.output)])
        arguments.extend(["--report", str(self.report), *extra])
        return subprocess.run(arguments, capture_output=True, text=True, timeout=90)

    def export(self):
        self.assertTrue(WORKER.is_file(), "IFC4 export/readback worker has not been implemented")
        result = self.run_cli()
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        report = json.loads(self.report.read_text(encoding="utf-8"))
        self.assertTrue(report["passed"], report)
        self.assertTrue(report["verified"], report)
        self.assertEqual(json.loads(result.stdout)["passed"], True)
        self.assertNotIn("products", json.loads(result.stdout))
        self.assertLess(len(result.stdout), 1500)
        return report

    def load_worker(self):
        self.assertTrue(WORKER.is_file(), "IFC4 export/readback worker has not been implemented")
        specification = importlib.util.spec_from_file_location("miki_ifc", WORKER)
        worker = importlib.util.module_from_spec(specification)
        specification.loader.exec_module(worker)
        return worker

    def start_lock_holder(self, targets):
        ready = self.directory / ("holder-" + uuid.uuid4().hex + ".ready")
        script = """
import importlib.util
from pathlib import Path
import json
import os
import sys

specification = importlib.util.spec_from_file_location("miki_ifc", sys.argv[1])
worker = importlib.util.module_from_spec(specification)
specification.loader.exec_module(worker)
targets = [Path(value) for value in json.loads(sys.argv[2])]
with worker.output_locks(targets, False):
    ready = Path(sys.argv[3])
    pending = ready.with_suffix(".tmp")
    pending.write_text(str(os.getpid()), encoding="utf-8")
    os.replace(pending, ready)
    sys.stdin.read()
"""
        process = subprocess.Popen(
            [sys.executable, "-B", "-c", script, str(WORKER), json.dumps([str(target) for target in targets]), str(ready)],
            stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True,
        )

        def stop_process():
            if process.poll() is None:
                if ready.exists():
                    os.kill(int(ready.read_text()), signal.SIGTERM if os.name == "nt" else signal.SIGKILL)
                else:
                    process.kill()
            process.communicate(timeout=15)

        self.addCleanup(stop_process)
        deadline = time.monotonic() + 15
        while not ready.exists() and process.poll() is None and time.monotonic() < deadline:
            time.sleep(0.02)
        if not ready.exists():
            stop_process()
            self.fail("Lock-holder subprocess did not acquire its targets")
        return process, int(ready.read_text())

    def reopen(self):
        import ifcopenshell
        return ifcopenshell.open(str(self.output))

    def product(self, report, semantic_id, role=None):
        matches = [entry for entry in report["products"] if entry["id"] == semantic_id and (role is None or entry["role"] == role)]
        self.assertEqual(len(matches), 1, matches)
        return matches[0]

    def assert_bounds(self, actual, minimum, maximum):
        for actual_value, expected_value in zip(actual["min"] + actual["max"], minimum + maximum):
            self.assertAlmostEqual(actual_value, expected_value, places=6)

    def test_exports_ifc4_and_reopens_all_solid_products(self):
        report = self.export()
        self.assertEqual(report["ifcSchema"], "IFC4")
        self.assertEqual(report["projectId"], "IFC-TEST")
        self.assertEqual(report["revision"], 0)
        self.assertEqual(report["engine"]["kernel"], "OpenCASCADE")
        self.assertEqual(report["failedChecks"], 0)
        self.assertEqual(len(report["products"]), 14)
        self.assertTrue(all(item["volume"] > 0 and item["passed"] for item in report["products"]))
        model = self.reopen()
        self.assertEqual(len(model.by_type("IfcProject")), 1)
        self.assertEqual(len(model.by_type("IfcBuilding")), 1)
        self.assertEqual(len(model.by_type("IfcBuildingStorey")), 2)
        self.assertEqual(len(model.by_type("IfcSpace")), 2)
        self.assertEqual(len(model.by_type("IfcWall")), 3)
        self.assertFalse(model.by_type("IfcBuildingElementProxy"))
        self.assertFalse(model.by_type("IfcTessellatedItem"))
        self.assertFalse(model.by_type("IfcFacetedBrep"))
        self.assertEqual(model.by_type("IfcSIUnit")[0].Name, "METRE")

    def test_occ_measures_wall_opening_subtraction_and_centreline_bounds(self):
        report = self.export()
        wall = self.product(report, "W1")
        self.assertAlmostEqual(wall["volume"], 4.784, places=7)
        self.assertAlmostEqual(wall["grossVolume"], 5.12, places=7)
        self.assertAlmostEqual(wall["voidVolume"], 0.336, places=7)
        self.assert_bounds(wall["bounds"], [0, -0.1, 0], [8, 0.1, 3.2])
        opening = self.product(report, "O1", "opening")
        self.assert_bounds(opening["bounds"], [1, -0.1, 0.9], [2.2, 0.1, 2.3])
        door_wall = self.product(report, "W3")
        self.assertAlmostEqual(door_wall["volume"], 4.473, places=7)
        self.assert_bounds(door_wall["bounds"], [7.85, 0, 0], [8.15, 6, 2.8])
        self.assert_bounds(self.product(report, "O3", "fill")["bounds"], [7.94, 2, 0], [8.06, 2.9, 2.1])

    def test_slab_top_voids_space_volume_and_stepped_stair_formula(self):
        report = self.export()
        floor = self.product(report, "F2")
        self.assertAlmostEqual(floor["volume"], 10.5, places=6)
        self.assert_bounds(floor["bounds"], [0, 0, 2.95], [8, 6, 3.2])
        stair = self.product(report, "S1")
        self.assertAlmostEqual(stair["volume"], 5.95, places=6)
        self.assert_bounds(stair["bounds"], [1.5, 1.25, 0], [5, 2.25, 3.2])
        self.assertAlmostEqual(self.product(report, "ROOM-2")["volume"], 153.6, places=6)

    def test_shared_types_use_the_same_mapping_and_void_fill_relations(self):
        report = self.export()
        model = self.reopen()
        window_type = model.by_type("IfcWindowType")[0]
        windows = model.by_type("IfcWindow")
        self.assertEqual(len(windows), 2)
        for window in windows:
            self.assertEqual(window.IsTypedBy[0].RelatingType, window_type)
            self.assertEqual(window.Representation.Representations[0].RepresentationType, "MappedRepresentation")
            mapped = window.Representation.Representations[0].Items[0]
            self.assertTrue(mapped.is_a("IfcMappedItem"))
            self.assertEqual(mapped.MappingSource, window_type.RepresentationMaps[0])
            self.assertEqual(len(window.FillsVoids), 1)
            opening = window.FillsVoids[0].RelatingOpeningElement
            self.assertEqual(len(opening.VoidsElements), 1)
            self.assertTrue(opening.VoidsElements[0].RelatingBuildingElement.is_a("IfcWall"))
        frame = 0.06
        expected_window_volume = (1.2 * 1.4 - (1.2 - 2 * frame) * (1.4 - 2 * frame)) * 0.15 + (1.2 - 2 * frame) * (1.4 - 2 * frame) * 0.012
        self.assertAlmostEqual(self.product(report, "O1", "fill")["volume"], expected_window_volume, places=7)

    def test_block_specs_matches_native_full_width_rails_and_between_rail_jambs(self):
        worker = self.load_worker()
        for kind, panel_depth in (("window", 0.012), ("door", 0.04)):
            with self.subTest(kind=kind):
                specification = {"id": "T", "kind": kind, "form": "rectangular", "width": 1.2, "height": 1.4, "depth": 0.15}
                boxes = worker.block_specs(specification)
                self.assertEqual(len(boxes), 5)
                self.assert_bounds(boxes[0], [0, -0.075, 0], [1.2, 0.075, 0.06])
                self.assert_bounds(boxes[1], [0, -0.075, 1.34], [1.2, 0.075, 1.4])
                self.assert_bounds(boxes[2], [0, -0.075, 0.06], [0.06, 0.075, 1.34])
                self.assert_bounds(boxes[4], [0.06, -panel_depth / 2, 0.06], [1.14, panel_depth / 2, 1.34])

    def test_global_ids_are_uuid5_and_stable_across_reexport(self):
        first = self.export()
        namespace = uuid.uuid5(uuid.NAMESPACE_URL, "IFC-TEST")
        import ifcopenshell.guid
        self.assertEqual(self.product(first, "W1")["globalId"], ifcopenshell.guid.compress(uuid.uuid5(namespace, "W1").hex))
        second_output = self.directory / "second.ifc"
        second_report = self.directory / "second.json"
        worker = self.load_worker()
        second = worker.export_project(self.source, second_output, second_report)
        self.assertEqual({(item["id"], item["role"]): item["globalId"] for item in first["products"]}, {(item["id"], item["role"]): item["globalId"] for item in second["products"]})

    def test_type_width_update_changes_both_instances_and_host_voids_only(self):
        before = self.export()
        self.project["types"][0]["width"] = 1.5
        self.project["revision"] = 1
        self.save_source()
        result = self.run_cli("export", "--overwrite")
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        after = json.loads(self.report.read_text())
        self.assertEqual(after["revision"], 1)
        for semantic_id in ("O1", "O2"):
            self.assertAlmostEqual(self.product(after, semantic_id, "opening")["volume"], 0.42, places=7)
            for role in ("opening", "fill"):
                self.assertEqual(self.product(before, semantic_id, role)["globalId"], self.product(after, semantic_id, role)["globalId"])
                self.assertGreater(self.product(after, semantic_id, role)["volume"], self.product(before, semantic_id, role)["volume"])
        self.assertAlmostEqual(self.product(after, "W1")["volume"], 4.7, places=7)
        for semantic_id in ("W3", "F1", "F2", "S1", "ROOM-1", "ROOM-2"):
            self.assertEqual(self.product(before, semantic_id), self.product(after, semantic_id))

    def test_only_l2_height_update_preserves_l1_geometry_and_all_identities(self):
        before = self.export()
        self.project["levels"][1]["height"] = 3.5
        self.project["revision"] = 1
        self.save_source()
        result = self.run_cli("export", "--overwrite")
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        after = json.loads(self.report.read_text())
        self.assert_bounds(self.product(after, "W2")["bounds"], [0, -0.1, 3.2], [8, 0.1, 6.7])
        self.assertAlmostEqual(self.product(after, "W2")["volume"], 5.264, places=7)
        for semantic_id in ("W1", "W3", "F1", "F2", "S1", "ROOM-1"):
            self.assertEqual(self.product(before, semantic_id), self.product(after, semantic_id))
        for entry in before["products"]:
            self.assertEqual(entry["globalId"], self.product(after, entry["id"], entry["role"])["globalId"])

    def test_diagonal_wall_uses_left_normal_and_left_edge_offset(self):
        self.project["walls"][2].update(start=[10, 10], end=[13, 14], thickness=0.4)
        self.save_source()
        report = self.export()
        self.assert_bounds(self.product(report, "O3", "opening")["bounds"], [11.04, 11.48, 0], [11.9, 12.44, 2.1])

    def test_full_height_edge_opening_updates_remaining_wall_bounds(self):
        self.project["types"][0]["height"] = 3.2
        for opening in self.project["openings"][:2]:
            opening.update(offset=0, sill=0)
        self.save_source()
        report = self.export()
        self.assert_bounds(self.product(report, "W1")["bounds"], [1.2, -0.1, 0], [8, 0.1, 3.2])

    def test_small_type_caps_frame_and_glazing_without_negative_geometry(self):
        self.project["types"][0].update(width=0.1, height=0.1, depth=0.005)
        self.save_source()
        report = self.export()
        self.assertAlmostEqual(self.product(report, "O1", "fill")["volume"], 0.00005, places=9)

    def test_omitted_type_depth_defaults_to_point_twelve_in_validation_and_cli(self):
        worker = self.load_worker()
        for definition in self.project["types"]:
            definition.pop("depth")
        normalized = worker.validate_project(self.project)
        self.assertEqual([definition["depth"] for definition in normalized["types"]], [0.12, 0.12])
        self.assertTrue(all("depth" not in definition for definition in self.project["types"]))
        self.save_source()
        report = self.export()
        window = self.product(report, "O1", "fill")
        self.assertAlmostEqual(window["volume"], 0.0523008, places=7)
        self.assert_bounds(window["bounds"], [1, -0.06, 0.9], [2.2, 0.06, 2.3])

    def test_generated_part_budget_accepts_five_thousand_and_excludes_level_metadata(self):
        worker = self.load_worker()
        source = project_at_generated_limit()
        worker.validate_project(source)
        source["levels"].append({"id": "L3", "elevation": 6.4, "height": 3.2})
        worker.validate_project(source)
        source["grids"].append({"id": "OVER", "start": [0, 0], "end": [8, 0]})
        with self.assertRaises(worker.WorkerError) as failure:
            worker.validate_project(source)
        self.assertEqual(failure.exception.code, "invalid_project")
        self.assertIn("5001", str(failure.exception))
        self.assertIn("5000", str(failure.exception))

    def test_generated_budget_matches_domain_weights_before_detailed_geometry(self):
        worker = self.load_worker()
        mutations = [
            ("grid", 5001, lambda source: source["grids"].append({"id": "NEW", "start": [0, 0], "end": [8, 0]})),
            ("space", 5001, lambda source: source["spaces"].append({**source["spaces"][0], "id": "NEW"})),
            ("wall", 5002, lambda source: source["walls"].append({**source["walls"][0], "id": "NEW"})),
            ("slab", 5002, lambda source: source["slabs"].append({**source["slabs"][0], "id": "NEW"})),
            ("opening", 5002, lambda source: source["openings"].append({**source["openings"][0], "id": "NEW"})),
            ("type", 5005, lambda source: source["types"].append({**source["types"][0], "id": "NEW"})),
            ("riser", 5001, lambda source: source["stairs"][0].update(risers=17)),
            ("flight", 5017, lambda source: source["stairs"].append({**source["stairs"][0], "id": "NEW"})),
        ]
        for name, estimate, mutation in mutations:
            with self.subTest(name=name):
                source = project_at_generated_limit()
                mutation(source)
                source["spaces"][0]["boundary"] = [[0, 0], [1, 1], [0, 1], [1, 0]]
                with self.assertRaises(worker.WorkerError) as failure:
                    worker.validate_project(source)
                self.assertEqual(failure.exception.code, "invalid_project")
                self.assertIn(str(estimate), str(failure.exception))
                self.assertIn("5000", str(failure.exception))
                self.assertIn("generated", str(failure.exception))

    def test_stair_expansion_budget_rejects_large_sources_and_direct_cli(self):
        worker = self.load_worker()
        for copies, risers, estimate in ((6000, 4, 30030), (5, 1000, 5035), (9000, 1000, 9009030)):
            with self.subTest(copies=copies, risers=risers):
                source = project_fixture()
                source["stairs"] = [{**source["stairs"][0], "id": f"ST-{index}", "risers": risers} for index in range(copies)]
                source["spaces"][0]["boundary"] = [[0, 0], [1, 1], [0, 1], [1, 0]]
                with self.assertRaises(worker.WorkerError) as failure:
                    worker.validate_project(source)
                self.assertIn(str(estimate), str(failure.exception))
                self.assertIn("5000", str(failure.exception))
        self.project["stairs"] = [{**self.project["stairs"][0], "id": f"ST-{index}", "risers": 1000} for index in range(5)]
        self.save_source()
        result = self.run_cli()
        self.assertEqual(result.returncode, 2, result.stdout + result.stderr)
        response = json.loads(result.stdout)
        self.assertFalse(response["verified"])
        self.assertEqual(response["error"]["code"], "invalid_project")
        self.assertIn("5035", response["error"]["message"])
        self.assertFalse(self.output.exists())
        self.assertFalse(self.report.exists())
        self.assertEqual(sorted(entry.name for entry in self.directory.iterdir()), ["project.json"])

    def test_rejects_malformed_source_before_publishing_outputs(self):
        mutations = {
            "wrong_units": lambda source: source.update(units="mm"),
            "schema": lambda source: source.update(schemaVersion=2),
            "unknown_root": lambda source: source.update(materials=[]),
            "unknown_wall_field": lambda source: source["walls"][0].update(material="concrete"),
            "duplicate_id": lambda source: source["walls"][0].update(id="L1"),
            "non_ascii_id": lambda source: source["walls"][0].update(id="墙"),
            "nan": lambda source: source["levels"][0].update(elevation=float("nan")),
            "infinite": lambda source: source["walls"][0].update(thickness=float("inf")),
            "huge_integer": lambda source: source["walls"][0].update(thickness=10 ** 500),
            "boolean_number": lambda source: source["walls"][0].update(thickness=True),
            "negative": lambda source: source["slabs"][0].update(thickness=-1),
            "zero_wall": lambda source: source["walls"][0].update(end=[0, 0]),
            "unresolved_level": lambda source: source["walls"][0].update(level="missing"),
            "unresolved_host": lambda source: source["openings"][0].update(wall="missing"),
            "unresolved_type": lambda source: source["openings"][0].update(type="missing"),
            "oversize_opening": lambda source: source["openings"][0].update(offset=7.5),
            "negative_sill": lambda source: source["openings"][0].update(sill=-0.1),
            "overlap": lambda source: source["openings"].append({"id": "OX", "wall": "W1", "type": "WIN-A", "offset": 1.5, "sill": 1}),
            "bow_tie": lambda source: source["slabs"][0].update(boundary=[[0, 0], [4, 4], [0, 4], [4, 0]]),
            "outside_void": lambda source: source["slabs"][0].update(voids=[[[7, 5], [9, 5], [9, 7], [7, 7]]]),
            "overlapping_voids": lambda source: source["slabs"][1]["voids"].append([[2, 1.5], [4, 1.5], [4, 2], [2, 2]]),
            "zero_direction": lambda source: source["stairs"][0].update(direction=[0, 0]),
            "reverse_stair": lambda source: source["stairs"][0].update(to="L1"),
            "fractional_risers": lambda source: source["stairs"][0].update(risers=3.5),
            "excessive_risers": lambda source: source["stairs"][0].update(risers=1000000),
            "blocked_stair": lambda source: source["slabs"][1].update(voids=[]),
        }
        worker = self.load_worker()
        for name, mutation in mutations.items():
            with self.subTest(name=name):
                source = project_fixture()
                mutation(source)
                with self.assertRaises(worker.WorkerError) as failure:
                    worker.validate_project(source)
                self.assertEqual(failure.exception.code, "invalid_project")
        self.assertFalse(self.output.exists())
        self.assertFalse(self.report.exists())

    def test_cli_rejects_duplicate_json_keys_with_structured_error(self):
        self.assertTrue(WORKER.is_file(), "IFC4 export/readback worker has not been implemented")
        self.source.write_text('{"id":"one","id":"two"}')
        result = self.run_cli()
        self.assertNotEqual(result.returncode, 0)
        response = json.loads(result.stdout)
        self.assertFalse(response["passed"])
        self.assertEqual(response["error"]["code"], "invalid_project")
        self.assertFalse(self.output.exists())
        self.assertFalse(self.report.exists())

    def test_refuses_overwrite_of_either_output_unless_explicit(self):
        worker = self.load_worker()
        for existing in (self.output, self.report):
            with self.subTest(existing=existing.name):
                existing.write_bytes(b"original user file")
                with self.assertRaises(worker.WorkerError) as failure:
                    worker.export_project(self.source, self.output, self.report)
                self.assertEqual(failure.exception.code, "output_exists")
                self.assertEqual(existing.read_bytes(), b"original user file")
                self.assertFalse((self.report if existing == self.output else self.output).exists())
                existing.unlink()

    def test_explicit_overwrite_replaces_both_files_after_verification(self):
        self.output.write_bytes(b"old model")
        self.report.write_bytes(b"old report")
        result = self.run_cli("export", "--overwrite")
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertTrue(self.output.read_bytes().startswith(b"ISO-10303-21;"))
        self.assertTrue(json.loads(self.report.read_text())["passed"])

    def test_rejects_aliased_source_model_and_report_even_with_overwrite(self):
        worker = self.load_worker()
        original = self.source.read_bytes()
        pairs = [(self.source, self.report), (self.output, self.source), (self.output, self.output)]
        for output, report in pairs:
            with self.subTest(output=output.name, report=report.name):
                with self.assertRaises(worker.WorkerError):
                    worker.export_project(self.source, output, report, overwrite=True)
                self.assertEqual(self.source.read_bytes(), original)
        alias = self.directory / "source-alias.ifc"
        os.link(self.source, alias)
        with self.assertRaises(worker.WorkerError):
            worker.export_project(self.source, alias, self.report, overwrite=True)
        self.assertEqual(self.source.read_bytes(), original)

    def test_invalid_input_does_not_replace_preexisting_files(self):
        worker = self.load_worker()
        self.output.write_bytes(b"old model")
        self.report.write_bytes(b"old report")
        self.project["walls"][0]["thickness"] = -1
        self.save_source()
        with self.assertRaises(worker.WorkerError):
            worker.export_project(self.source, self.output, self.report, overwrite=True)
        self.assertEqual(self.output.read_bytes(), b"old model")
        self.assertEqual(self.report.read_bytes(), b"old report")

    def test_real_readback_failure_prevents_publication_and_preserves_existing_files(self):
        worker = self.load_worker()
        self.output.write_bytes(b"old model")
        self.report.write_bytes(b"old report")
        original_inspect = worker.inspect_project

        def corrupt_then_inspect(project, path):
            import ifcopenshell
            model = ifcopenshell.open(str(path))
            wall = next(item for item in model.by_type("IfcWall") if item.Name == "W1")
            wall.Representation.Representations[0].Items[0].Depth = 0.2
            model.write(str(path))
            return original_inspect(project, path)

        with mock.patch.object(worker, "inspect_project", side_effect=corrupt_then_inspect):
            with self.assertRaises(worker.WorkerError) as failure:
                worker.export_project(self.source, self.output, self.report, overwrite=True)
        self.assertEqual(failure.exception.code, "verification_failed")
        self.assertEqual(self.output.read_bytes(), b"old model")
        self.assertEqual(self.report.read_bytes(), b"old report")

    def test_second_publish_failure_rolls_back_both_existing_files(self):
        worker = self.load_worker()
        self.output.write_bytes(b"old model")
        self.report.write_bytes(b"old report")
        real_replace = os.replace
        failed = False

        def fail_report_once(source, destination):
            nonlocal failed
            if Path(destination) == self.report and not failed:
                failed = True
                raise OSError("injected report publication failure")
            return real_replace(source, destination)

        with mock.patch.object(worker.os, "replace", side_effect=fail_report_once):
            with self.assertRaises(worker.WorkerError):
                worker.export_project(self.source, self.output, self.report, overwrite=True)
        self.assertTrue(failed)
        self.assertEqual(self.output.read_bytes(), b"old model")
        self.assertEqual(self.report.read_bytes(), b"old report")
        self.assertEqual(sorted(item.name for item in self.directory.iterdir()),
                         ["model.ifc", "model.ifc.miki-ifc.lock", "project.json", "report.json", "report.json.miki-ifc.lock"])

    def test_second_staging_failure_cleans_up_first_temporary_file(self):
        worker = self.load_worker()
        real_mkstemp = tempfile.mkstemp
        allocations = 0

        def fail_second_allocation(*arguments, **keywords):
            nonlocal allocations
            allocations += 1
            if allocations == 2:
                raise OSError("injected second staging allocation failure")
            return real_mkstemp(*arguments, **keywords)

        with mock.patch.object(worker.tempfile, "mkstemp", side_effect=fail_second_allocation):
            with self.assertRaises(worker.WorkerError):
                worker.export_project(self.source, self.output, self.report)
        self.assertEqual(sorted(entry.name for entry in self.directory.iterdir()),
                         ["model.ifc.miki-ifc.lock", "project.json", "report.json.miki-ifc.lock"])

    def test_killed_process_releases_output_locks_and_retry_exports(self):
        holder, holder_pid = self.start_lock_holder([self.output, self.report])
        markers = [target.with_name(target.name + ".miki-ifc.lock") for target in (self.output, self.report)]
        identities = [marker.stat().st_ino for marker in markers]
        result = self.run_cli()
        self.assertEqual(result.returncode, 2, result.stdout + result.stderr)
        self.assertEqual(json.loads(result.stdout)["error"]["code"], "output_busy")
        self.assertIsNone(holder.poll())
        os.kill(holder_pid, signal.SIGTERM if os.name == "nt" else signal.SIGKILL)
        holder.communicate(timeout=15)
        self.assertFalse(self.output.exists())
        self.assertFalse(self.report.exists())
        self.export()
        self.assertEqual([marker.stat().st_ino for marker in markers], identities)

    def test_live_output_or_report_owner_survives_competing_lock_attempts(self):
        worker = self.load_worker()
        for target in (self.output, self.report):
            with self.subTest(target=target.name):
                holder, holder_pid = self.start_lock_holder([target])
                marker = target.with_name(target.name + ".miki-ifc.lock")
                identity = marker.stat().st_ino
                for attempt in range(2):
                    result = self.run_cli()
                    self.assertEqual(result.returncode, 2, result.stdout + result.stderr)
                    self.assertEqual(json.loads(result.stdout)["error"]["code"], "output_busy")
                    self.assertEqual(marker.stat().st_ino, identity)
                    self.assertIsNone(holder.poll())
                if target == self.report:
                    with worker.output_locks([self.output], False):
                        pass
                with self.assertRaises(worker.WorkerError) as failure:
                    with worker.output_locks([target], False):
                        self.fail("A live owner's OS lock must stay exclusive")
                self.assertEqual(failure.exception.code, "output_busy")
                holder.communicate(timeout=15)
                self.assertEqual(holder.returncode, 0)
                with worker.output_locks([self.output, self.report], False):
                    pass
                self.assertEqual(marker.stat().st_ino, identity)

    def test_orphan_empty_markers_are_reusable_and_persist_without_replacement(self):
        markers = [target.with_name(target.name + ".miki-ifc.lock") for target in (self.output, self.report)]
        for marker in markers:
            marker.write_bytes(b"")
        identities = [marker.stat().st_ino for marker in markers]
        self.export()
        result = self.run_cli()
        self.assertEqual(result.returncode, 2, result.stdout + result.stderr)
        self.assertEqual(json.loads(result.stdout)["error"]["code"], "output_exists")
        self.assertEqual([marker.stat().st_ino for marker in markers], identities)

    def test_inspect_retries_after_report_lock_holder_is_killed(self):
        self.export()
        self.report.unlink()
        holder, holder_pid = self.start_lock_holder([self.report])
        result = self.run_cli("inspect")
        self.assertEqual(result.returncode, 2, result.stdout + result.stderr)
        self.assertEqual(json.loads(result.stdout)["error"]["code"], "output_busy")
        os.kill(holder_pid, signal.SIGTERM if os.name == "nt" else signal.SIGKILL)
        holder.communicate(timeout=15)
        result = self.run_cli("inspect")
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertTrue(json.loads(self.report.read_text())["verified"])

    def test_no_overwrite_publication_race_preserves_new_report_and_removes_model(self):
        worker = self.load_worker()
        real_link = os.link

        def create_report_before_link(source, destination):
            if Path(destination) == self.report:
                self.report.write_bytes(b"another writer's report")
            return real_link(source, destination)

        with mock.patch.object(worker.os, "link", side_effect=create_report_before_link):
            with self.assertRaises(worker.WorkerError):
                worker.export_project(self.source, self.output, self.report)
        self.assertEqual(self.report.read_bytes(), b"another writer's report")
        self.assertFalse(self.output.exists())

    def test_concurrent_no_overwrite_publish_never_clobbers_existing_result(self):
        self.assertTrue(WORKER.is_file(), "IFC4 export/readback worker has not been implemented")
        arguments = [sys.executable, str(WORKER), "export", "--project", str(self.source), "--output", str(self.output), "--report", str(self.report)]
        processes = [subprocess.Popen(arguments, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True) for _ in range(2)]
        results = [(process.communicate(timeout=90), process.returncode) for process in processes]
        self.assertEqual(sorted(result[1] for result in results), [0, 2], results)
        self.assertTrue(json.loads(self.report.read_text())["passed"])
        self.assertEqual(len(self.reopen().by_type("IfcWall")), 3)

    def test_inspect_rejects_corrupted_relationships_and_geometric_changes(self):
        self.export()
        worker = self.load_worker()
        original = self.output.read_bytes()
        mutations = {
            "void": lambda model: model.remove(model.by_type("IfcRelVoidsElement")[0]),
            "fill": lambda model: model.remove(model.by_type("IfcRelFillsElement")[0]),
            "type": lambda model: model.remove(model.by_type("IfcRelDefinesByType")[0]),
            "storey": lambda model: model.remove(model.by_type("IfcRelContainedInSpatialStructure")[0]),
            "wall_solid": lambda model: setattr(model.by_type("IfcWall")[0].Representation.Representations[0].Items[0], "Depth", 2.5),
            "fill_mapping": lambda model: setattr(model.by_type("IfcWindow")[0].Representation.Representations[0].Items[0], "MappingSource", model.by_type("IfcDoorType")[0].RepresentationMaps[0]),
            "duplicate_guid": lambda model: setattr(model.by_type("IfcWall")[1], "GlobalId", model.by_type("IfcWall")[0].GlobalId),
            "storey_elevation": lambda model: setattr(model.by_type("IfcBuildingStorey")[0], "Elevation", 9.0),
            "type_predefined_kind": lambda model: setattr(model.by_type("IfcWindowType")[0], "PredefinedType", "USERDEFINED"),
        }
        for name, mutation in mutations.items():
            with self.subTest(name=name):
                self.output.write_bytes(original)
                model = self.reopen()
                mutation(model)
                model.write(str(self.output))
                report = worker.inspect_project(self.source, self.output)
                self.assertFalse(report["passed"], report)
                self.assertFalse(report["verified"], report)
                self.assertGreater(report["failedChecks"], 0)

    def test_inspect_cli_reports_failed_checks_and_returns_failure(self):
        self.export()
        model = self.reopen()
        model.remove(model.by_type("IfcRelFillsElement")[0])
        model.write(str(self.output))
        self.report.unlink()
        result = self.run_cli("inspect")
        self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
        report = json.loads(self.report.read_text())
        self.assertFalse(report["passed"])
        self.assertTrue(any(not check["passed"] for check in report["checks"]))

    def test_inspect_rejects_products_outside_source_inventory(self):
        self.export()
        worker = self.load_worker()
        model = self.reopen()
        import ifcopenshell.guid
        model.create_entity("IfcBeam", GlobalId=ifcopenshell.guid.new(), Name="UNMANAGED")
        model.write(str(self.output))
        report = worker.inspect_project(self.source, self.output)
        self.assertFalse(report["verified"], report)

    def test_inspect_verifies_metre_units_even_without_physical_products(self):
        self.project = project_fixture()
        for collection in ("grids", "spaces", "walls", "slabs", "types", "openings", "stairs"):
            self.project[collection] = []
        self.save_source()
        self.export()
        worker = self.load_worker()
        model = self.reopen()
        model.by_type("IfcSIUnit")[0].Prefix = "MILLI"
        model.write(str(self.output))
        report = worker.inspect_project(self.source, self.output)
        self.assertFalse(report["verified"], report)

    def test_inspect_wrong_schema_and_truncated_files_fail_with_structured_results(self):
        import ifcopenshell
        ifcopenshell.file(schema="IFC2X3").write(str(self.output))
        result = self.run_cli("inspect")
        self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
        self.assertFalse(json.loads(self.report.read_text())["verified"])
        self.report.unlink()
        self.output.write_bytes(b"ISO-10303-21;\ntruncated")
        result = self.run_cli("inspect")
        self.assertEqual(result.returncode, 2, result.stdout + result.stderr)
        response = json.loads(result.stdout)
        self.assertFalse(response["verified"])
        self.assertEqual(response["error"]["code"], "invalid_ifc")
        self.assertFalse(self.report.exists())

    def test_rotated_stair_uses_normalized_direction_and_left_normal(self):
        self.project["stairs"][0].update(origin=[20, 20], direction=[0, -9])
        for slab in self.project["slabs"]:
            slab["boundary"] = [[19, 15], [23, 15], [23, 22], [19, 22]]
        self.project["slabs"][1]["voids"] = [[[19.75, 16.5], [21.25, 16.5], [21.25, 20.25], [19.75, 20.25]]]
        self.save_source()
        report = self.export()
        self.assert_bounds(self.product(report, "S1")["bounds"], [20, 16.5, 0], [21, 20, 3.2])

    def test_stair_connections_require_supported_landings_and_full_flight_void(self):
        worker = self.load_worker()
        mutations = {
            "missing_lower_slab": lambda source: source["slabs"].pop(0),
            "unsupported_lower_landing": lambda source: source["slabs"][0].update(boundary=[[1, 0], [8, 0], [8, 6], [1, 6]]),
            "unsupported_upper_landing": lambda source: source["slabs"][1].update(boundary=[[0, 0], [5.25, 0], [5.25, 6], [0, 6]]),
            "incomplete_flight_void": lambda source: source["slabs"][1].update(voids=[[[2, 1], [5, 1], [5, 2.5], [2, 2.5]]]),
        }
        for name, mutation in mutations.items():
            with self.subTest(name=name):
                source = project_fixture()
                mutation(source)
                with self.assertRaises(worker.WorkerError):
                    worker.validate_project(source)

    def test_supported_thousand_riser_flight_is_a_valid_exact_sweep(self):
        self.project["stairs"][0]["risers"] = 1000
        self.save_source()
        report = self.export()
        self.assertAlmostEqual(self.product(report, "S1")["volume"], 5.6056, places=6)
        self.assertEqual(self.reopen().by_type("IfcStairFlight")[0].NumberOfRisers, 1000)

    def test_closed_clockwise_concave_polygon_and_nonzero_slab_offset(self):
        self.project["stairs"] = []
        self.project["slabs"][0].update(boundary=[[0, 0], [0, 4], [2, 4], [2, 2], [4, 2], [4, 0], [0, 0]], offset=0.5)
        self.save_source()
        report = self.export()
        slab = self.product(report, "F1")
        self.assertAlmostEqual(slab["volume"], 3.0, places=6)
        self.assert_bounds(slab["bounds"], [0, 0, 0.25], [4, 4, 0.5])

    def test_canonical_demo_reopens_with_expected_counts_and_stair_volume(self):
        self.project = json.loads((WAREHOUSE / "examples" / "two-storey.json").read_text(encoding="utf-8-sig"))
        self.save_source()
        report = self.export()
        self.assertEqual(len(report["products"]), 35)
        self.assertEqual(report["counts"]["IfcWall"], 10)
        self.assertEqual(report["counts"]["IfcWindow"], 6)
        self.assertEqual(report["counts"]["IfcDoor"], 3)
        self.assertAlmostEqual(self.product(report, "SLAB-L2")["volume"], 17.926, places=6)
        self.assertAlmostEqual(self.product(report, "STAIR-L1-L2")["volume"], 8.917333333333333, places=6)

    def test_inspect_checks_source_revision_and_refuses_report_overwrite(self):
        self.export()
        original = self.report.read_bytes()
        result = self.run_cli("inspect")
        self.assertEqual(result.returncode, 2, result.stdout + result.stderr)
        self.assertEqual(self.report.read_bytes(), original)
        self.project["revision"] = 99
        self.save_source()
        result = self.run_cli("inspect", "--overwrite")
        self.assertEqual(result.returncode, 1, result.stdout + result.stderr)

    def test_missing_runtime_returns_actionable_compact_json(self):
        self.assertTrue(WORKER.is_file(), "IFC4 export/readback worker has not been implemented")
        result = self.run_cli(isolated=True)
        self.assertEqual(result.returncode, 2)
        response = json.loads(result.stdout)
        self.assertFalse(response["passed"])
        self.assertEqual(response["error"]["code"], "runtime_missing")
        self.assertIn("requirements.txt", response["error"]["message"])
        self.assertFalse(self.output.exists())

    def test_dependency_pin_matches_verified_runtime(self):
        from importlib.metadata import version
        requirements = WAREHOUSE / "python" / "requirements.txt"
        self.assertTrue(requirements.is_file(), "The worker must pin the installed IFC runtime")
        pins = [line.strip() for line in requirements.read_text().splitlines() if line.strip()]
        self.assertIn("ifcopenshell==" + version("ifcopenshell"), pins)


if __name__ == "__main__":
    unittest.main()
