"""Inventory installed game codes and export an auditable UTF-8 YAML map.

Requires UnityPy (the local investigation copy at obj/tools/python is supported).
No per-code aliases: labels come from native metadata, static condition data,
or a direct Lua condition -> visible option association. Never executes Lua.
"""
import argparse
import collections
import datetime
import hashlib
import json
import pathlib
import re
import struct
import sys
from code_name_documents import write_documents

ROOT = pathlib.Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "obj/tools/python"))
import UnityPy


class Reader:
    def __init__(self, obj):
        self.obj = obj
        self.raw = obj.get_raw_data()
        self.pos = 28
        self.name = self.string()

    def integer(self):
        value = struct.unpack_from("<i", self.raw, self.pos)[0]
        self.pos += 4
        return value

    def string(self):
        size = self.integer()
        if not 0 <= size <= len(self.raw) - self.pos:
            raise ValueError("Invalid serialized string length")
        value = self.raw[self.pos:self.pos + size].decode("utf-8")
        self.pos = (self.pos + size + 3) // 4 * 4
        return value

    def boolean(self):
        value = self.raw[self.pos] != 0
        self.pos = (self.pos + 4) // 4 * 4
        return value

    def pointer(self):
        file_id, path_id = struct.unpack_from("<iq", self.raw, self.pos)
        self.pos += 12
        if not path_id:
            return None
        asset = pathlib.Path(self.obj.assets_file.name).name
        if file_id:
            asset = self.obj.assets_file.externals[file_id - 1].path.rsplit("/", 1)[-1]
        return asset.lower(), path_id


def label(text, key=""):
    text = re.sub(r"<[^>]*>", "", text or "").strip().split("\n")[0].strip()
    if not text or text.casefold() == key.casefold() or text.startswith(("Flag/", "Story/")):
        return ""
    return text if re.search(r"[\u3400-\u9fff]", text) else ""


def export(game, output, analysis_output=None, enums_json=None, sources_output=None):
    data = game / "Mortal_Data"
    objects, classes, catalog, scripts, locales = {}, {}, {}, {}, {}
    ambiguous_locales = set()
    conversion = dict(re.findall(r'\.Replace\("(.)", "(.)"\)', (ROOT / "OptionImpactLocalization.cs").read_text(encoding="utf-8-sig")))
    simplify = lambda text: "".join(conversion.get(c, c) for c in text)

    # Inventory every serialized asset file, rather than a chosen list of IDs.
    for path in sorted(data.glob("*.assets")):
        env = UnityPy.load(str(path))
        for obj in env.objects:
            if obj.type.name == "TextAsset":
                text = obj.read()
                content = text.m_Script.lstrip("\ufeff")
                if text.m_Name.endswith("zh-cn") or (text.m_Name.startswith("Story_")
                        and sum(content.count(c) for c in "门说师云药兰") > sum(content.count(c) for c in "門說師雲藥蘭")
                        and not re.search(r"[\uac00-\ud7af]", content)):
                    for line in content.splitlines():
                        if " = " in line:
                            key, value = line.split(" = ", 1)
                            if key in locales and locales[key] != value:
                                ambiguous_locales.add(key)
                                locales.pop(key)
                            if key not in ambiguous_locales:
                                locales[key] = value
                if "checkpointmanager." in content or "statmodifymanager." in content:
                    old = scripts.get(text.m_Name.lower())
                    if old is not None and old != content:
                        # Story scripts for multiple languages must not be
                        # silently merged. Resources contains the loaded Lua.
                        if path.name != "resources.assets":
                            continue
                    scripts[text.m_Name.lower()] = content
            if obj.type.name != "MonoBehaviour":
                continue
            asset = obj.read(check_read=False)
            try:
                cls = asset.m_Script.deref().read().m_ClassName
            except (ValueError, FileNotFoundError):
                continue
            pointer = (path.name.lower(), obj.path_id)
            objects[pointer], classes[pointer] = obj, cls

    scopes = {"FlagData": "flags", "ConditionResultData": "conditions", "SwitchResultData": "switches",
              "PositionEventData": "events", "MissionCheckData": "missions", "MissionData": "mission_groups"}
    for pointer, cls in classes.items():
        if cls not in scopes:
            continue
        rd = Reader(objects[pointer])
        code = rd.name
        if not code:
            continue
        source = f"{pointer[0]}:{pointer[1]} ({cls})"
        native = ""
        if cls in ("FlagData", "ConditionResultData", "SwitchResultData", "PositionEventData"):
            native = rd.string()
        elif cls == "MissionCheckData":
            rd.string()  # key
            rd.boolean()  # active
            native = rd.string()  # description
        elif cls == "MissionData":
            native_name, native_description = rd.string(), rd.string()
            native = label(native_description, code) or native_name
        title = label(locales.get("Flag/Name/" + code, ""), code) if cls == "FlagData" else ""
        title = title or label(native, code)
        scope = scopes[cls]
        key = (scope, code)
        if key in catalog:
            if title and catalog[key]["name"] and simplify(title) != catalog[key]["name"]:
                catalog[key].update(name="", status="ambiguous")
            elif title and catalog[key]["status"] == "unresolved":
                catalog[key].update(name=simplify(title), status="native")
            catalog[key]["sources"].append(source)
            continue
        catalog[key] = {"name": simplify(title), "status": "native" if title else "unresolved", "sources": [source]}
        if cls == "PositionEventData" and not title:
            mapping = rd.pointer()
            if mapping in objects:
                mapped = label(Reader(objects[mapping]).name)
                if mapped:
                    catalog[key].update(name=simplify(mapped), status="native_reference")
                    catalog[key]["sources"].append(f"_config -> {mapping[0]}:{mapping[1]}")

    def source_value(rd):
        kind, constant = rd.integer(), rd.integer()
        pointers = [rd.pointer() for _ in range(3)]
        low, high = rd.integer(), rd.integer()
        pointers += [rd.pointer() for _ in range(5)]
        negative = rd.boolean()
        pointers += [rd.pointer() for _ in range(5)]
        return {"kind": kind, "constant": constant, "pointers": pointers,
                "low": low, "high": high, "negative": negative}

    groups = {}
    def group(pointer):
        if pointer in groups:
            return groups[pointer]
        if pointer not in objects or classes[pointer] != "StatGroupVariable":
            return None
        rd = Reader(objects[pointer])
        operation = rd.integer()
        count = rd.integer()
        result = operation, [source_value(rd) for _ in range(count)]
        if rd.pos != len(rd.raw):
            raise ValueError("Unsupported StatValueReference layout: " + rd.name)
        groups[pointer] = result
        return result

    def flag_code(pointer):
        info = group(pointer)
        if not info or info[0] not in (1, 2, 3, 4) or len(info[1]) != 1:
            return None
        source = info[1][0]
        target = source["pointers"][2]
        if source["kind"] != 3 or source["negative"] or target not in objects:
            return None
        return Reader(objects[target]).name

    def group_label(pointer):
        info = group(pointer)
        if not info or info[0] not in (1, 2, 3, 4) or len(info[1]) != 1:
            return None
        source = info[1][0]
        kind, pointers = source["kind"], source["pointers"]
        text = None
        if kind == 0:
            text = str(source["constant"])
        elif kind == 1 and pointers[0] in objects:
            rd = Reader(objects[pointers[0]])
            rd.integer(); rd.integer()
            text = label(rd.string())
        elif kind == 2 and pointers[1] in objects:
            rd = Reader(objects[pointers[1]])
            rd.integer()
            text = label(rd.string())
            if text:
                text += "好感"
        elif kind == 3:
            code = flag_code(pointer)
            text = catalog.get(("flags", code), {}).get("name") or None
        elif kind == 7:
            text = f"随机判定({source['low']}～{source['high']})"
        # Other source types require additional game-specific context; leave
        # them unresolved instead of interpreting an asset name as a meaning.
        if text and source["negative"]:
            text = "−(" + text + ")" if kind else str(-source["constant"])
        return simplify(text) if text else None

    conditions = {}
    definitions = {}
    for pointer, cls in classes.items():
        if cls not in ("ConditionResultData", "SwitchResultData"):
            continue
        rd = Reader(objects[pointer]); native_note = rd.string(); execution_count = rd.integer()
        counted = rd.boolean() if cls == "SwitchResultData" else False
        items = []
        for _ in range(rd.integer()):
            note, logic = rd.string(), rd.integer()
            comparisons = [(rd.integer(), rd.pointer(), rd.pointer()) for _ in range(rd.integer())]
            items.append((note, logic, comparisons))
        count_items = []
        if cls == "SwitchResultData":
            count_items = [(rd.integer(), rd.integer()) for _ in range(rd.integer())]
        if rd.pos != len(rd.raw):
            raise ValueError("Unsupported checkpoint layout: " + rd.name)
        conditions[rd.name] = items if cls == "ConditionResultData" else conditions.get(rd.name, [])
        scope = scopes[cls]
        definitions[(scope, rd.name)] = {"note": native_note, "execution_count": execution_count,
                                       "counted": counted, "items": items, "count_items": count_items}
        entry = catalog[(scope, rd.name)]
        if entry["name"] or entry["status"] == "ambiguous" or counted or len(items) != 1:
            continue
        note, logic, comparisons = items[0]
        text = label(note, rd.name)
        if not text:
            parts = []
            for operator, left, right in comparisons:
                a, b = group_label(left), group_label(right)
                if not a or not b or not 0 <= operator < 6:
                    parts = []
                    break
                parts.append(a + " " + ["=", "≠", "<", "≤", ">", "≥"][operator] + " " + b)
            text = (" 且 " if logic == 0 else " 或 ").join(parts)
        if text and len(text) <= 64:
            entry.update(name=text, status="condition_summary")
            entry["sources"].append("单一条件组的完整静态表达式（不执行判定）")

    # Detect direct availability gates in all Lua scripts. A unique option
    # caption identifies the gate; no arbitrary code-prefix rules are used.
    gates = collections.defaultdict(list)
    gate_call = re.compile(r'local\s+(\w+)\s*=\s*checkpointmanager\.Condition\("([^"]+)"\)')
    for script, lua in scripts.items():
        for call in gate_call.finditer(lua):
            variable, code = call.groups()
            pattern = re.compile(r'=\s*' + re.escape(variable) + r'\s+and\s+"(?:move|event|talk)\+([^+"\n]+)\+[^"\n]*"\s+or\s+"~x"')
            # Stop before reassigning the local variable; a later use can have
            # unrelated meaning in another scope.
            tail = lua[call.end():]
            reassign = re.search(r'\b' + re.escape(variable) + r'\s*=', tail)
            if reassign:
                tail = tail[:reassign.start()]
            for match in pattern.finditer(tail):
                option = match.group(1)
                caption = label(locales.get("Story/" + option, ""), option)
                if caption:
                    line = lua.count("\n", 0, call.end() + match.start()) + 1
                    gates[code].append((simplify(caption), f"story/{script}.lua:{line} -> Story/{option}"))
    for code, links in gates.items():
        captions = {caption for caption, _ in links}
        entry = catalog.get(("conditions", code))
        if len(captions) != 1 or entry is None or entry["status"] in ("native", "ambiguous"):
            continue
        caption = next(iter(captions))
        entry.update(name=caption + "可用条件", status="option_gate")
        entry["sources"] += sorted({source for _, source in links})
        items = conditions.get(code, [])
        if len(items) == 1 and len(items[0][2]) == 1:
            operator, left, right = items[0][2][0]
            flag = flag_code(left)
            constant = group_label(right)
            if flag and constant == "1" and operator in (0, 5):
                flag_entry = catalog.get(("flags", flag))
                if flag_entry is not None and flag_entry["status"] == "unresolved":
                    flag_entry.update(name=caption + "可用标记", status="option_gate_flag")
                    flag_entry["sources"] += entry["sources"]

    if analysis_output:
        relevant = {"StatGroupVariable", "GameStat", "RelationshipStat", "FlagData", "Book",
                    "Misc", "SpecialItem", "PlayerTalentData", "GameTimeData", "UpgradeItemData",
            "BookDataCollection", "LibraryItemData", "ConditionResultData", "SwitchResultData"}
        current_time = []
        current_stats = []
        for pointer, cls in classes.items():
            if cls == "PlayerStatManagerData":
                manager = Reader(objects[pointer])
                time_pointer = manager.pointer()
                if time_pointer:
                    current_time.append(f"{time_pointer[0]}:{time_pointer[1]}")
                manager.pointer()  # time collection
                for _ in range(9):
                    manager.string()
                manager.integer()  # default actions
                stats_pointer = manager.pointer()
                if stats_pointer in objects:
                    if classes[stats_pointer] != "PlayerStatData":
                        raise ValueError("Player manager stats reference has the wrong type")
                    stat_data = Reader(objects[stats_pointer])
                    for _ in range(stat_data.integer()):
                        stat_pointer = stat_data.pointer()
                        if stat_pointer:
                            current_stats.append(f"{stat_pointer[0]}:{stat_pointer[1]}")
                    if stat_data.pos != len(stat_data.raw):
                        raise ValueError("Unsupported player stat collection layout")
        analysis_output.parent.mkdir(parents=True, exist_ok=True)
        analysis_output.write_text(json.dumps({
            "catalog": {scope: {code: entry for (s, code), entry in catalog.items() if s == scope}
                        for scope in sorted(set(scopes.values()))},
            "definitions": {scope: {code: value for (s, code), value in definitions.items() if s == scope}
                            for scope in ("conditions", "switches")},
            "objects": {f"{p[0]}:{p[1]}": {"class": cls, "name": Reader(objects[p]).name,
                                             "raw": objects[p].get_raw_data().hex(),
                                             "externals": [x.path.rsplit("/", 1)[-1].lower()
                                                           for x in objects[p].assets_file.externals]}
                        for p, cls in classes.items() if cls in relevant},
            "scripts": scripts, "locales": locales, "conversion": conversion,
            "roles": {"current_time": sorted(set(current_time)), "current_stats": sorted(set(current_stats))},
            "enums": json.loads(enums_json.read_text(encoding="utf-8-sig")) if enums_json else {}}, ensure_ascii=False), encoding="utf-8")

    counts = {scope: {"total": sum(s == scope for s, _ in catalog),
                      "named": sum(s == scope and bool(e['name']) for (s, _), e in catalog.items())}
              for scope in sorted(set(scopes.values()))}
    document = {"version": 1, "locale": "zh-CN",
                "generated_at": datetime.datetime.now(datetime.timezone.utc).isoformat(),
                "statistics": counts,
                "data_hashes": {filename: hashlib.sha256((data / filename).read_bytes()).hexdigest()
                                for filename in ("globalgamemanagers.assets", "sharedassets2.assets", "resources.assets")},
                "mappings": {scope: {code: entry for (category, code), entry in sorted(catalog.items()) if category == scope}
                             for scope in sorted(counts)}}
    provenance_output = write_documents(document, output, sources_output)
    print(json.dumps({"output": str(output), "sources_output": str(provenance_output), "statistics": counts, "total": len(catalog),
                      "named": sum(bool(e['name']) for e in catalog.values())}, ensure_ascii=False))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game-dir", type=pathlib.Path, default=pathlib.Path(r"C:\Game\Steam\steamapps\common\LegendOfMortal"))
    parser.add_argument("--output", type=pathlib.Path, default=ROOT / "artifacts/LOMSaveProbe/LOMSaveProbe.CodeNames.yaml")
    parser.add_argument("--sources-output", type=pathlib.Path, help="Optional developer sources output path")
    parser.add_argument("--analysis-output", type=pathlib.Path, help="Optional complete static checkpoint context inventory")
    parser.add_argument("--enums-json", type=pathlib.Path, help="Native enum metadata exported by Update-CodeNameContexts.ps1")
    args = parser.parse_args()
    export(args.game_dir, args.output, args.analysis_output, args.enums_json, args.sources_output)
