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
    code_files = sorted([*(root / "src/Miki.IceWorld").glob("*.cs"), *(root / "src/Miki.IceWorld.Rhino").glob("*.cs"),
                         *(root / "src/Miki.IceWorld.Grasshopper").glob("*.cs")], key=lambda p: p.relative_to(root).as_posix())
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
        if (item["units"] != "m" or item["stage"] not in ("concept-test-assumptions", "standard-candidate")
                or item["runtime"] != "rhino9-net8-grasshopper"):
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


def validate_standard_baseline(root):
    """Check that the standards traceability layer is machine-readable and explicit."""
    errors = []
    path = root / "policies/iceworld-standard-baseline.json"
    try:
        baseline = read_json(path)
    except (OSError, ValueError, json.JSONDecodeError) as error:
        return ["Standard baseline cannot be read — " + str(error)]
    if set(baseline) != {"schemaVersion", "jurisdiction", "scope", "reviewStatus", "standards", "profiles", "manualReview"}:
        return ["Standard baseline fields do not match schema"]
    if baseline["schemaVersion"] != 1 or baseline["jurisdiction"] != "CN-HLJ":
        errors.append("Unsupported standard baseline schema or jurisdiction")
    if baseline["reviewStatus"] not in ("candidate", "approved"):
        errors.append("Unknown standard baseline review state")
    if not isinstance(baseline["scope"], str) or not baseline["scope"]:
        errors.append("Standard baseline scope is required")
    standards = baseline["standards"]
    required_standards = {"GB 51202-2016", "GB 55031-2022", "GB 50352-2019", "GB 55037-2022",
                          "GB 55019-2021", "DB23/T 3792-2024", "HLJ-ICE-GUIDE-2023"}
    if not isinstance(standards, list) or not required_standards.issubset({item.get("id") for item in standards if isinstance(item, dict)}):
        errors.append("Required Chinese standards are missing")
    else:
        for item in standards:
            if set(item) != {"id", "title", "status", "appliesTo", "source", "verifiedOn"}:
                errors.append("Standard fields do not match schema — " + str(item.get("id")))
                continue
            if item["status"] != "current" or not item["title"] or not item["appliesTo"]:
                errors.append("Standard is not a current, named requirement — " + item["id"])
            if not re.fullmatch(r"https?://\S+", item["source"]):
                errors.append("Standard source is not a URL — " + item["id"])
            if not re.fullmatch(r"\d{4}-\d{2}-\d{2}", item["verifiedOn"]):
                errors.append("Standard verification date is invalid — " + item["id"])
    profile = baseline["profiles"].get("temporary-exhibition") if isinstance(baseline["profiles"], dict) else None
    if not isinstance(profile, dict) or not isinstance(profile.get("dimensions"), dict):
        errors.append("temporary-exhibition dimension profile is missing")
    else:
        dimensions = profile["dimensions"]
        expected = {"treadGoing", "riserHeight", "stairWidth", "guardHeight", "stairFlightSteps", "outdoorEgressStairPitchDegrees"}
        if set(dimensions) != expected:
            errors.append("Standard dimension profile fields do not match schema")
        for name, dimension in dimensions.items():
            if not isinstance(dimension, dict) or "clause" not in dimension or "applicability" not in dimension:
                errors.append("Dimension rule lacks traceability — " + name)
                continue
            numeric = {key: value for key, value in dimension.items() if key not in ("clause", "applicability")}
            if not numeric or any(not isinstance(value, (int, float)) for value in numeric.values()):
                errors.append("Dimension rule has invalid numeric bounds — " + name)
    reviews = baseline["manualReview"]
    if not isinstance(reviews, list) or not reviews or any(not isinstance(item, dict) or not item.get("id") or not item.get("requirement") or not item.get("reason") for item in reviews):
        errors.append("Manual review obligations are incomplete")
    return errors


def evaluate_design_profile(root, index):
    """Evaluate declared defaults against the candidate design profile.

    These are generator guardrails, not an assertion that the whole building or a
    project-specific circulation route complies with a Standard.
    """
    baseline = read_json(root / "policies/iceworld-standard-baseline.json")
    dimensions = baseline["profiles"]["temporary-exhibition"]["dimensions"]
    findings = []
    for item in index["examples"]:
        source = read_json((root / item["source"]).resolve())
        values = source["parameters"]
        kind = item["kind"]
        if kind in ("straight-stair", "switchback-stair"):
            flights = 2 if kind == "switchback-stair" else 1
            single_rise = values.get("rise", 1.8) / flights
            steps_per_flight = values.get("risers", 12)
            going = values.get("run", 3.6) / steps_per_flight
            riser = single_rise / steps_per_flight
            width = values.get("width", 1.5)
            guard = values.get("railHeight", 1.1)
            checks = [
                ("treadGoing", going >= dimensions["treadGoing"]["minimumM"],
                 "stair going {:.3f} m is below {:.2f} m".format(going, dimensions["treadGoing"]["minimumM"])),
                ("riserHeight", riser <= dimensions["riserHeight"]["maximumM"],
                 "stair riser {:.3f} m exceeds {:.2f} m".format(riser, dimensions["riserHeight"]["maximumM"])),
                ("stairWidth", width >= dimensions["stairWidth"]["minimumM"],
                 "stair width {:.3f} m is below {:.2f} m".format(width, dimensions["stairWidth"]["minimumM"])),
                ("guardHeight", guard >= dimensions["guardHeight"]["minimumM"],
                 "guard height {:.3f} m is below {:.2f} m".format(guard, dimensions["guardHeight"]["minimumM"])),
                ("stairFlightSteps", dimensions["stairFlightSteps"]["minimum"] <= steps_per_flight <= dimensions["stairFlightSteps"]["maximum"],
                 "stair flight has {} steps; profile permits {}-{}".format(
                     steps_per_flight, dimensions["stairFlightSteps"]["minimum"], dimensions["stairFlightSteps"]["maximum"])),
            ]
            findings.extend({"id": item["id"], "check": check, "passed": passed, "message": message}
                            for check, passed, message in checks)
        elif kind == "platform-railing":
            guard = values.get("railHeight", 1.1)
            findings.append({"id": item["id"], "check": "guardHeight",
                             "passed": guard >= dimensions["guardHeight"]["minimumM"],
                             "message": "guard height {:.3f} m is below {:.2f} m".format(guard, dimensions["guardHeight"]["minimumM"])})
    return findings


if __name__ == "__main__":
    root = pathlib.Path(__file__).resolve().parents[1]
    index = read_json(root / "examples/index.json")
    errors = validate(root, index)
    errors += validate_standard_baseline(root)
    findings = evaluate_design_profile(root, index)
    gated_ids = {item["id"] for item in index["examples"] if item["stage"] == "standard-candidate"}
    errors += [finding["id"] + " standard candidate: " + finding["message"]
               for finding in findings if finding["id"] in gated_ids and not finding["passed"]]
    print("\n".join(errors) if errors else "PASS warehouse index")
    sys.exit(bool(errors))
