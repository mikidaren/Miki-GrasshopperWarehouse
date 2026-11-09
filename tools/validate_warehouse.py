"""Validate warehouse metadata and artifact references using the standard library."""
import json
import hashlib
import pathlib
import re
import sys
import xml.etree.ElementTree as ET


def read_json(path):
    def unique(pairs):
        result = {}
        for name, value in pairs:
            if name in result:
                raise ValueError("Duplicate JSON property " + name)
            result[name] = value
        return result
    return json.loads(path.read_text(encoding="utf-8-sig"), object_pairs_hook=unique)


def validate(root, index):
    errors = []
    root = root.resolve()
    if index.get("schemaVersion") != 1 or not isinstance(index.get("examples"), list):
        return ["Unsupported warehouse index schema"]
    categories = index.get("categories", [])
    if len(set(categories)) != len(categories):
        errors.append("Duplicate category")
    seen = set()
    source_paths = set()
    definition_paths = set()
    report_path = root / "docs/iceworld-native-validation.json"
    report = read_json(report_path) if report_path.is_file() else {"results": []}
    evidence = {entry["id"]: entry for entry in report["results"]}
    code_files = sorted([*(root / "src/Miki.IceWorld").glob("*.cs"), *(root / "src/Miki.IceWorld.Grasshopper").glob("*.cs")], key=lambda p: p.relative_to(root).as_posix())
    code_hashes = "\n".join(p.relative_to(root).as_posix() + " " + hashlib.sha256(p.read_bytes()).hexdigest() for p in code_files)
    kernel_hash = hashlib.sha256(code_hashes.encode()).hexdigest()
    required = {"id", "kind", "title", "category", "tags", "source", "definition", "units", "runtime", "stage", "validation"}
    for item in index["examples"]:
        if set(item) != required:
            errors.append("Index entry fields do not match schema")
            continue
        label = item["id"]
        if not isinstance(label, str) or not re.fullmatch(r"[A-Z0-9][A-Z0-9._-]{0,127}", label):
            errors.append("Invalid stable ID")
            continue
        if label in seen:
            errors.append("Duplicate example ID " + label)
        seen.add(label)
        if item["category"] not in categories:
            errors.append(label + " has unknown category")
        if item["units"] != "m" or item["stage"] != "concept-test-assumptions" or item["runtime"] != "rhino9-net8-grasshopper":
            errors.append(label + " has unsupported units, runtime or stage")
        if not isinstance(item["tags"], list) or not item["tags"] or any(not isinstance(tag, str) or not re.fullmatch(r"[a-z0-9-]{1,64}", tag) for tag in item["tags"]):
            errors.append(label + " has invalid tags")
        validation = item["validation"]
        if not isinstance(validation, dict) or set(validation) != {"portable", "native", "grasshopper", "sourceSha256", "definitionSha256"}:
            errors.append(label + " has invalid validation fields")
            continue
        for check in ("portable", "native", "grasshopper"):
            if validation[check] not in ("passed", "pending", "failed"):
                errors.append(label + " has unknown validation state")
        for field, suffix, paths in (("source", ".json", source_paths), ("definition", ".ghx", definition_paths)):
            relative = item[field]
            if not isinstance(relative, str) or not re.fullmatch(r"examples/iceworld/[a-z0-9]+(?:-[a-z0-9]+)*" + re.escape(suffix), relative):
                errors.append(label + " has invalid artifact path")
                continue
            path = (root / relative).resolve()
            if not path.is_relative_to(root) or not path.is_file():
                errors.append(label + " artifact path missing or outside warehouse")
                continue
            if relative in paths:
                errors.append(label + " has Duplicate artifact path")
            paths.add(relative)
            digest_key = "sourceSha256" if field == "source" else "definitionSha256"
            if validation["native"] == "passed" or validation["grasshopper"] == "passed":
                digest = hashlib.sha256(path.read_bytes()).hexdigest()
                if validation[digest_key] != digest:
                    errors.append(label + " has stale validation hash for " + field)
                record = evidence.get(label, {})
                if record.get(digest_key) != digest or report.get("kernelSha256") != kernel_hash:
                    errors.append(label + " has stale or missing native evidence")
            try:
                if field == "source":
                    source = read_json(path)
                    if set(source) != {"id", "kind", "parameters"} or source["id"] != label or source["kind"] != item["kind"]:
                        errors.append(label + " source identity or fields mismatch")
                else:
                    tree = ET.parse(path)
                    if tree.getroot().tag != "Archive":
                        errors.append(label + " definition is not a GH archive")
            except (ValueError, ET.ParseError) as error:
                errors.append(label + " artifact cannot be parsed — " + str(error))
    actual_sources = {p.relative_to(root).as_posix() for p in (root / "examples/iceworld").glob("*.json")}
    actual_definitions = {p.relative_to(root).as_posix() for p in (root / "examples/iceworld").glob("*.ghx")}
    if source_paths != actual_sources or definition_paths != actual_definitions:
        errors.append("Unindexed or missing example artifact paths")
    return errors


if __name__ == "__main__":
    root = pathlib.Path(__file__).resolve().parents[1]
    errors = validate(root, read_json(root / "examples/index.json"))
    print("\n".join(errors) if errors else "PASS warehouse index")
    sys.exit(bool(errors))
