import copy
import importlib.util
import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("warehouse_validator", ROOT / "tools/validate_warehouse.py")
validator = importlib.util.module_from_spec(spec)
spec.loader.exec_module(validator)


class WarehouseTests(unittest.TestCase):
    def setUp(self):
        self.index = validator.read_json(ROOT / "examples/index.json")

    def test_real_index_and_six_sources(self):
        self.assertEqual(validator.validate(ROOT, self.index), [])

    def test_duplicate_id_rejected(self):
        self.index["examples"].append(copy.deepcopy(self.index["examples"][0]))
        self.assertTrue(any("Duplicate" in e for e in validator.validate(ROOT, self.index)))

    def test_path_escape_rejected(self):
        self.index["examples"][0]["source"] = "../outside.json"
        self.assertTrue(any("path" in e for e in validator.validate(ROOT, self.index)))

    def test_unknown_category_rejected(self):
        self.index["examples"][0]["category"] = "unknown"
        self.assertTrue(any("category" in e for e in validator.validate(ROOT, self.index)))

    def test_stale_native_claim_rejected(self):
        item = self.index["examples"][0]
        item["validation"]["native"] = "passed"
        item["validation"]["sourceSha256"] = "0" * 64
        self.assertTrue(any("stale" in e for e in validator.validate(ROOT, self.index)))

    def test_unknown_validation_state_rejected(self):
        self.index["examples"][0]["validation"]["grasshopper"] = "probably"
        self.assertTrue(any("state" in e for e in validator.validate(ROOT, self.index)))

    def test_standard_baseline_is_traceable_and_explicitly_candidate(self):
        self.assertEqual(validator.validate_standard_baseline(ROOT), [])

    def test_missing_specialist_ice_standard_rejected(self):
        baseline = validator.read_json(ROOT / "policies/iceworld-standard-baseline.json")
        baseline["standards"] = [item for item in baseline["standards"] if item["id"] != "GB 51202-2016"]
        import tempfile
        with tempfile.TemporaryDirectory() as directory:
            temporary = pathlib.Path(directory)
            (temporary / "policies").mkdir()
            (temporary / "policies/iceworld-standard-baseline.json").write_text(
                __import__("json").dumps(baseline, ensure_ascii=False), encoding="utf-8")
            self.assertTrue(any("Required" in error for error in validator.validate_standard_baseline(temporary)))

if __name__ == "__main__":
    unittest.main()
