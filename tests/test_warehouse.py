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


if __name__ == "__main__":
    unittest.main()
