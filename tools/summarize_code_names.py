"""Fill blank checkpoint names from a complete static inventory and Lua context.

Input baseline JSON must be produced by a standard YAML parser (YamlDotNet).
Only blank conditions/switches are changed. No code-prefix aliases, Lua execution,
or game-state mutation is used. Every candidate and unresolved entry is audited.
"""
import argparse
import collections
import datetime
import json
import os
import pathlib
import re
import struct
from dataclasses import dataclass
from code_name_documents import merge_sources, write_documents


OPS = ("=", "≠", "<", "≤", ">", "≥")


class Reader:
    def __init__(self, key, obj):
        self.asset = key.split(":")[0]
        self.obj = obj
        self.raw = bytes.fromhex(obj["raw"])
        self.pos = 28
        self.name = self.string()

    def integer(self):
        value = struct.unpack_from("<i", self.raw, self.pos)[0]
        self.pos += 4
        return value

    def boolean(self):
        value = bool(self.raw[self.pos])
        self.pos = (self.pos + 4) // 4 * 4
        return value

    def string(self):
        size = self.integer()
        if not 0 <= size <= len(self.raw) - self.pos:
            raise ValueError("Invalid string in " + self.name)
        value = self.raw[self.pos:self.pos + size].decode("utf-8")
        self.pos = (self.pos + size + 3) // 4 * 4
        return value

    def pointer(self):
        file_id, path_id = struct.unpack_from("<iq", self.raw, self.pos)
        self.pos += 12
        if not path_id:
            return None
        asset = self.obj["externals"][file_id - 1] if file_id else self.asset
        return f"{asset}:{path_id}"


@dataclass
class Expression:
    text: str
    complete: bool
    subjects: tuple = ()
    unknown: tuple = ()


class ContextNames:
    def __init__(self, inventory, baseline):
        self.data = inventory
        self.map = baseline["mappings"]
        self.objects = inventory["objects"]
        self.locales = inventory["locales"]
        self.groups = {}
        self.calls = collections.defaultdict(list)
        self.gates = collections.defaultdict(list)
        self.script_contexts()
        self.safe_gate_flags = self.validate_gate_flags()

    def clean(self, text):
        text = re.sub(r"<[^>]*>", "", text or "").strip().split("\n")[0].strip()
        return "".join(self.data["conversion"].get(c, c) for c in text)

    def localized(self, key):
        value = self.clean(self.locales.get(key, ""))
        return value if re.search(r"[\u3400-\u9fff]", value) else ""

    def caption(self, value):
        parts = value.split("+")
        key = parts[1] if len(parts) >= 3 and re.fullmatch(r"[a-z]+", parts[0]) else parts[-1]
        if len(parts) == 3 and parts[:2] == ["talk", "talk"]:
            person = self.localized("Position/title/" + parts[2]) or self.localized("Character/" + parts[2])
            return "与" + person + "对话" if person else ""
        result = self.localized("Story/" + key)
        if result:
            return result
        # Old scripts also contain literal captions. A bare code is not a name.
        # '~' marks unavailable dice choices, and '|<=70' is a roll limit.
        key = key.split("|")[0].lstrip("~")
        return self.clean(key) if re.search(r"[\u3400-\u9fff]", key) else ""

    def script_contexts(self):
        checkpoint = re.compile(r'checkpointmanager\.(Condition|Switch)\(\s*"([^"]+)"\s*\)')
        assignment = re.compile(r'^(\w+)\[(\d+)\]\s*=.*?"([^"\n]+)"')
        choose = re.compile(r'(?:local\s+)?(\w+)\s*=\s*choose(?:timer)?\(\s*(\w+)')
        branch = re.compile(r'^(?:if|elseif)\s+(\w+)\s*==\s*(\d+)\s+then')
        # Generated story scripts are line-oriented; scopes prevent a previous
        # option marker from leaking into a later, unrelated checkpoint.
        for script, lua in self.data["scripts"].items():
            tables, choices, stack, pending = {}, {}, [], {}
            lines = lua.splitlines()
            for number, raw in enumerate(lines, 1):
                line = raw.strip()
                if not line or line.startswith("--"):
                    continue
                if re.match(r"^(end\b|until\b)", line):
                    if stack:
                        stack.pop()
                elif re.match(r"^(elseif\b|else$)", line):
                    if stack:
                        stack[-1] = ""
                elif re.match(r"^(if\b|for\b|while\b|repeat\b)", line):
                    stack.append("")
                match = assignment.match(line)
                if match:
                    table, index, value = match.groups()
                    tables[table, index] = self.caption(value) if value != "~x" else ""
                    gate = re.search(r'=\s*(\w+)\s+and\s+"([^"\n]+)"\s+or\s+"~x"', line)
                    if gate and gate.group(1) in pending:
                        caption = self.caption(gate.group(2))
                        if caption:
                            self.gates[pending[gate.group(1)]].append((caption, f"story/{script}.lua:{number}"))
                match = choose.search(line)
                if match:
                    variable, table = match.groups()
                    # Snapshot choices: later table writes must not change an
                    # already selected option's meaning.
                    choices[variable] = {i: caption for (t, i), caption in tables.items() if t == table}
                match = branch.match(line)
                if match and stack:
                    variable, index = match.groups()
                    stack[-1] = choices.get(variable, {}).get(index, "")
                for match in checkpoint.finditer(line):
                    method, code = match.groups()
                    scope = "conditions" if method == "Condition" else "switches"
                    self.calls[scope, code].append({
                        "source": f"story/{script}.lua:{number}",
                        "choice": next((s for s in reversed(stack) if s), ""),
                        "before": self.dialogue_context(lines[max(0, number - 12):number - 1])[-2:],
                        "after": self.dialogue_context(lines[number:number + 35])[:4],
                    })
                    variable = re.match(r'(?:local\s+)?(\w+)\s*=', line)
                    if variable and method == "Condition":
                        pending[variable.group(1)] = code
                # A re-assigned variable must not remain an availability gate.
                variable = re.match(r'(?:local\s+)?(\w+)\s*=', line)
                if variable and not checkpoint.search(line):
                    pending.pop(variable.group(1), None)

    def dialogue_context(self, lines):
        result = []
        for line in lines:
            # Dialogue strings are supporting evidence, never blindly used as
            # a checkpoint title. Their proximity alone does not prove purpose.
            if not re.search(r"\bsay\s*\(|saymanager\.|dialog", line, re.I):
                continue
            for text in re.findall(r'"([^"\n]+)"', line):
                text = self.localized("Story/" + text) or self.clean(text)
                if re.search(r"[\u3400-\u9fff]", text):
                    result.append(text)
        return result

    def object_reader(self, pointer):
        return Reader(pointer, self.objects[pointer]) if pointer in self.objects else None

    def single_source(self, pointer):
        key = f"{pointer[0]}:{pointer[1]}" if isinstance(pointer, list) else pointer
        rd = self.object_reader(key)
        if not rd or self.objects[key]["class"] != "StatGroupVariable":
            return None
        operation, count = rd.integer(), rd.integer()
        if operation not in (1, 2, 3, 4) or count != 1:
            return None
        kind, constant = rd.integer(), rd.integer()
        pointers = [rd.pointer() for _ in range(3)]
        rd.integer(); rd.integer()
        pointers += [rd.pointer() for _ in range(5)]
        negative = rd.boolean()
        return kind, constant, pointers, negative

    def validate_gate_flags(self):
        meanings = collections.defaultdict(set)
        for code, definition in self.data["definitions"]["conditions"].items():
            for _, _, comparisons in definition["items"]:
                for _, left, _ in comparisons:
                    source = self.single_source(left)
                    if source and source[0] == 3 and not source[3]:
                        flag = self.object_reader(source[2][2])
                        if flag:
                            meanings[flag.name].update(caption for caption, _ in self.gates[code])
        return {flag for flag, captions in meanings.items() if len(captions) == 1}

    def atomic(self, source, seen):
        kind, constant, pointers, low, high, negative = source
        text, subject = "", ""
        if kind == 0:
            text = str(-constant if negative else constant)
        elif kind == 7:
            text, subject = f"随机值({low}～{high})", "随机判定"
        elif kind == 8:
            return self.group(pointers[7], seen)
        elif kind == 16:
            rd = self.object_reader(pointers[12])
            code = rd.name if rd else "?"
            text = self.map["conditions"].get(code, {}).get("name", "")
            if text:
                text = f"条件「{text}」的真假值"
                subject = "关联条件"
            else:
                return Expression(f"条件({code})", False, unknown=(code,))
        else:
            index = {1: 0, 2: 1, 3: 2, 4: 3, 5: 4, 6: 5, 9: 8,
                     10: 6, 11: 3, 12: 9, 13: 9, 14: 10, 15: 11}.get(kind)
            rd = self.object_reader(pointers[index]) if index is not None else None
            if rd:
                if kind == 1:
                    stat_type = rd.integer(); rd.integer()
                    note = self.clean(rd.string())
                    enum = self.data["enums"].get("GameStatType", {}).get(str(stat_type), {})
                    text = self.localized("PlayerStat/" + enum.get("key", "")) or note or self.clean(enum.get("name", ""))
                    if not note and pointers[0] not in self.data["roles"]["current_stats"] and text:
                        # A shared stat type does not identify its owner.
                        # Preserve the asset identifier for non-player stats.
                        text += "(" + rd.name + ")"
                elif kind == 2:
                    relationship_type = rd.integer()
                    note = self.clean(rd.string())
                    enum = self.data["enums"].get("RelationshipStatType", {}).get(str(relationship_type), {})
                    person = self.clean(enum.get("name", "")) or note
                    text = person + "好感" if person else ""
                elif kind == 3:
                    entry = self.map["flags"].get(rd.name, {})
                    # Existing auto-generated option aliases are unsafe as a
                    # global flag meaning when that flag is reused elsewhere.
                    text = entry.get("name", "") if entry.get("status") != "option_gate_flag" or rd.name in self.safe_gate_flags else ""
                    if not text:
                        return Expression(rd.name, False, unknown=(rd.name,))
                elif kind in (4, 5, 6, 11):
                    note = self.clean(rd.string())  # ItemData developer note
                    code = rd.string()
                    prefix = {4: "Book", 5: "Misc", 6: "Special", 11: "Book"}[kind]
                    title = self.localized(prefix + "/Name/" + code) or note
                    if title:
                        text = title + ("等级" if kind == 11 else "数量")
                elif kind == 10:
                    code = rd.string()
                    title = self.localized("PlayerTalent/Name/" + code)
                    if title:
                        text = title + "等级"
                elif kind in (12, 13):
                    code = rd.string()
                    note = self.clean(rd.string())
                    title = self.localized("Equip/Name/" + code) or note
                    if re.search(r"[\u3400-\u9fff]", title):
                        text = title + ("开发状态" if kind == 12 else "开发等级")
                elif kind == 14:
                    text = "精通秘笈数量"
                elif kind == 15:
                    note, title = self.clean(rd.string()), rd.string()
                    title = self.localized("Library/Title/" + rd.name) or self.localized(title) or self.clean(title) or note
                    if re.search(r"[\u3400-\u9fff]", title):
                        text = f"风云史「{title}」数量"
                elif kind == 9:
                    # Only Year/Month/Stage/DayEnvironment are serialized;
                    # private initialization fields are not Unity fields.
                    year, month, stage, _ = [rd.integer() for _ in range(4)]
                    if pointers[8] in self.data["roles"]["current_time"]:
                        text = "当前日期"
                    elif year >= 1 and 1 <= month <= 12 and stage in (1, 2, 3):
                        text = f"第{year}年{month}月" + ("上旬", "中旬", "下旬")[stage - 1]
                subject = ("日期" if text == "当前日期" else "") if kind == 9 else text
                # The game's GetValue negates only constants/stats/favor/flags.
                if text and negative and kind in (1, 2, 3):
                    text = "−(" + text + ")"
            if not text:
                unknown = rd.name if rd else f"source-type-{kind}"
                return Expression(unknown, False, unknown=(unknown,))
        return Expression(self.clean(text), True, (self.clean(subject),) if subject else ())

    def group(self, pointer, seen=()):
        key = f"{pointer[0]}:{pointer[1]}" if isinstance(pointer, list) else pointer
        if key in self.groups:
            return self.groups[key]
        if key in seen:
            return Expression("循环引用", False, unknown=(key,))
        rd = self.object_reader(key)
        if not rd or self.objects[key]["class"] != "StatGroupVariable":
            return Expression("未知数值组", False, unknown=(key,))
        operation, count = rd.integer(), rd.integer()
        values = []
        for _ in range(count):
            kind, constant = rd.integer(), rd.integer()
            pointers = [rd.pointer() for _ in range(3)]
            low, high = rd.integer(), rd.integer()
            pointers += [rd.pointer() for _ in range(5)]
            negative = rd.boolean()
            pointers += [rd.pointer() for _ in range(5)]
            values.append(self.atomic((kind, constant, pointers, low, high, negative), seen + (key,)))
        if rd.pos != len(rd.raw):
            raise ValueError("Unsupported group layout " + rd.name)
        if not values or operation not in (1, 2, 3, 4):
            return Expression(rd.name, False, unknown=(rd.name,))
        if len(values) == 1:
            result = values[0]
        else:
            terms = [v.text for v in values]
            text = ("(" + (" + " if operation == 1 else " × ").join(terms) + ")"
                    if operation in (1, 4) else ("最大值" if operation == 2 else "最小值") + "(" + "、".join(terms) + ")")
            flat = tuple(dict.fromkeys(s for v in values for s in v.subjects))
            # Full arithmetic remains in evidence. Long lists get a concise
            # topic, without claiming a particular outcome or threshold.
            topic = text
            if (len(text) > 42 or (operation in (2, 3) and len(values) > 2)) and all(v.complete for v in values):
                prefix = os.path.commonprefix([v.text for v in values])
                if all(v.text.endswith("数量") for v in values) and operation == 1:
                    topic = (prefix if len(prefix) >= 2 else "物品") + "数量合计"
                elif all(v.text.endswith("好感") for v in values) and operation in (2, 3):
                    topic = "相关人物" + ("最高" if operation == 2 else "最低") + "好感"
                elif len(prefix) >= 2 and operation == 1:
                    topic = prefix.rstrip("（(、， ") + "相关标记合计"
                else:
                    topic = "、".join(flat)
            subjects = (topic,) if all(v.complete for v in values) else flat
            result = Expression(text, all(v.complete for v in values), subjects,
                                tuple(u for v in values for u in v.unknown))
        self.groups[key] = result
        return result

    def definition(self, scope, code):
        definition = self.data["definitions"][scope][code]
        if definition["counted"]:
            texts = ["触发次数 " + OPS[op] + " " + str(value) for op, value in definition["count_items"]]
            return {"complete": bool(texts), "expressions": texts, "subjects": ["触发次数"], "unknown": []}
        expressions, subjects, unknown = [], [], []
        complete = bool(definition["items"])
        for note, logic, comparisons in definition["items"]:
            terms = []
            for operator, left, right in comparisons:
                a, b = self.group(left), self.group(right)
                terms.append(a.text + " " + OPS[operator] + " " + b.text)
                complete &= a.complete and b.complete
                subjects += a.subjects + b.subjects
                unknown += a.unknown + b.unknown
            if not terms:
                complete = False  # No useful semantic name for an empty item.
            expressions.append((" 且 " if logic == 0 else " 或 ").join(terms))
        return {"complete": bool(complete), "expressions": expressions,
                "subjects": list(dict.fromkeys(subjects)), "unknown": list(dict.fromkeys(unknown))}

    def suggest(self, scope, code, info, calls):
        definition = self.data["definitions"][scope][code]
        expressions = info["expressions"]
        captions = sorted({caption for caption, _ in self.gates[code]}) if scope == "conditions" else []
        if (captions and len(captions) <= 2 and len("／".join(captions)) <= 45
                and not (info["complete"] and len(expressions) == 1 and len(expressions[0]) <= 52)):
            return "／".join(captions) + "可用条件", "context_gate"
        if info["complete"] and len(expressions) == 1 and len(expressions[0]) <= 52:
            return expressions[0], "context_expression"
        choices = {call["choice"] for call in calls}
        # Shared checkpoints without the same option context cannot inherit a
        # single option's label; empty contexts count as conflicts too.
        choice = next(iter(choices)) if len(choices) == 1 and "" not in choices else ""
        suffix = "分支" if scope == "switches" else "判定"
        subjects = info["subjects"]
        if info["complete"] and subjects:
            topic = "、".join(subjects)
            if len(topic) <= 58:
                return topic + suffix, "context_subjects"
        if choice and len(choice) <= 38:
            return "「" + choice + "」后续" + suffix, "context_option"
        # Some assets already use a descriptive Chinese object name as their
        # code. Use that literal context, without translating English prefixes.
        if re.fullmatch(r"[\u3400-\u9fff_]+", code) and len(code) <= 45:
            return self.clean(code).replace("_", "·"), "context_asset_name"
        return "", "unresolved"

    def apply(self):
        report = []
        for scope in ("conditions", "switches"):
            for code, entry in sorted(self.map[scope].items()):
                if entry["name"]:
                    continue
                info = self.definition(scope, code)
                calls = self.calls[scope, code]
                name, status = self.suggest(scope, code, info, calls)
                # Display names pass through rich-text tag stripping. ASCII
                # '< ... >' in combined comparisons would look like a tag.
                # Full-width comparison glyphs preserve the intended meaning.
                name = name.replace("<", "＜").replace(">", "＞")
                if entry.get("status") == "ambiguous":
                    name, status = "", "unresolved"
                record = {"scope": scope, "code": code, "name": name, "status": status,
                          "condition": info, "calls": calls}
                evidence = ["上下文归纳；静态读取，不执行剧情或随机判定"]
                evidence += [f"条件组{i}: {text}" for i, text in enumerate(info["expressions"], 1) if text]
                evidence += [c["source"] + (" -> 选项「" + c["choice"] + "」" if c["choice"] else "") for c in calls]
                if scope == "conditions":
                    evidence += [source + " -> 可用选项「" + caption + "」" for caption, source in self.gates[code]]
                if name:
                    if len(name) > 64:
                        raise ValueError("Name exceeds runtime limit: " + code)
                    entry.update(name=name, status=status)
                else:
                    record["reason"] = ("含未命名依赖，且没有唯一、明确的选项调用上下文" if info["unknown"]
                                        else "条件组为空或主题过长，缺少可靠的简短名称")
                    if entry.get("status") == "ambiguous":
                        record["reason"] = "同编号原生定义冲突，保留编号"
                    if entry.get("status") != "ambiguous":
                        entry["status"] = "context_unresolved"
                    evidence.append("保留编号：" + record["reason"])
                entry["sources"] = list(dict.fromkeys(entry.get("sources", []) + evidence))
                report.append(record)
        return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--inventory", type=pathlib.Path, required=True)
    parser.add_argument("--baseline-json", type=pathlib.Path, required=True)
    parser.add_argument("--baseline-sources-json", type=pathlib.Path,
                        help="Optional developer sources parsed to JSON with YamlDotNet")
    parser.add_argument("--output", type=pathlib.Path, required=True)
    parser.add_argument("--sources-output", type=pathlib.Path)
    parser.add_argument("--report", type=pathlib.Path, required=True)
    args = parser.parse_args()
    inventory = json.loads(args.inventory.read_text(encoding="utf-8-sig"))
    document = json.loads(args.baseline_json.read_text(encoding="utf-8-sig"))
    if args.baseline_sources_json:
        merge_sources(document, json.loads(args.baseline_sources_json.read_text(encoding="utf-8-sig")))
    analyzer = ContextNames(inventory, document)
    report = analyzer.apply()
    for scope, entries in document["mappings"].items():
        document["statistics"][scope] = {"total": len(entries), "named": sum(bool(e["name"]) for e in entries.values())}
    summary = {"reviewed": len(report), "added": sum(bool(e["name"]) for e in report),
               "unresolved": sum(not e["name"] for e in report),
               "methods": dict(collections.Counter(e["status"] for e in report))}
    document["context_analysis"] = {"updated_at": datetime.datetime.now(datetime.timezone.utc).isoformat(), **summary}
    write_documents(document, args.output, args.sources_output)
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps({"summary": summary, "entries": report}, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(summary, ensure_ascii=False))


if __name__ == "__main__":
    main()
