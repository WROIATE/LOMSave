"""Separate runtime names from developer provenance without losing evidence."""
import argparse
import copy
import json
import pathlib


def sources_path(output):
    return output.with_name(output.stem + ".Sources" + output.suffix)


def merge_sources(document, provenance):
    """Accept both legacy inline sources and the separate developer document."""
    if int(provenance.get("version", 0)) != 1:
        raise ValueError("Unsupported sources document version")
    for scope, entries in provenance["mappings"].items():
        for code, entry in entries.items():
            if scope not in document["mappings"] or code not in document["mappings"][scope]:
                raise ValueError("Sources have no matching runtime entry: " + scope + "/" + code)
            target = document["mappings"][scope][code]
            target["sources"] = list(dict.fromkeys(target.get("sources", []) + entry.get("sources", [])))
    if "context_analysis" in provenance:
        document.setdefault("context_analysis", copy.deepcopy(provenance["context_analysis"]))


def split_document(document):
    runtime = copy.deepcopy(document)
    provenance = {key: copy.deepcopy(document[key])
                  for key in ("version", "locale", "generated_at", "data_hashes") if key in document}
    if "context_analysis" in runtime:
        provenance["context_analysis"] = runtime.pop("context_analysis")
    provenance["mappings"] = {}
    for scope, entries in runtime["mappings"].items():
        provenance["mappings"][scope] = {}
        for code, entry in entries.items():
            provenance["mappings"][scope][code] = {"sources": entry.pop("sources", [])}
    return runtime, provenance


def write_yaml(document, output, comments):
    # JSON-quoted scalars are valid YAML. The caller validates with YamlDotNet.
    schema_keys = {"version", "locale", "generated_at", "statistics", "data_hashes", "mappings",
                   "conditions", "switches", "flags", "events", "missions", "mission_groups",
                   "total", "named", "name", "status", "sources", "context_analysis", "updated_at",
                   "reviewed", "added", "unresolved", "methods", "context_gate", "context_expression",
                   "context_subjects", "context_option", "context_asset_name"}
    lines = ["# " + comment for comment in comments]

    def emit(value, indent):
        prefix = " " * indent
        for key, child in value.items():
            key_text = key if key in schema_keys else json.dumps(str(key), ensure_ascii=False)
            if isinstance(child, dict):
                lines.append(prefix + key_text + ":" + (" {}" if not child else ""))
                emit(child, indent + 2)
            elif isinstance(child, list):
                lines.append(prefix + key_text + ":" + (" []" if not child else ""))
                for item in child:
                    lines.append(prefix + "  - " + json.dumps(item, ensure_ascii=False))
            else:
                lines.append(prefix + key_text + ": " + json.dumps(child, ensure_ascii=False))

    emit(document, 0)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text("\n".join(lines) + "\n", encoding="utf-8")


def write_documents(document, output, provenance_output=None):
    provenance_output = provenance_output or sources_path(output)
    if output.resolve() == provenance_output.resolve():
        raise ValueError("Runtime and sources outputs must be separate files")
    runtime, provenance = split_document(document)
    runtime["version"] = provenance["version"] = int(document["version"])
    # Untyped YamlDotNet baselines may represent unquoted integers as strings.
    # Keep the numeric metadata in the same YAML form as the original export.
    for counts in runtime.get("statistics", {}).values():
        for key in ("total", "named"):
            counts[key] = int(counts[key])
    if "context_analysis" in provenance:
        summary = provenance["context_analysis"]
        for key in ("reviewed", "added", "unresolved"):
            summary[key] = int(summary[key])
        summary["methods"] = {key: int(value) for key, value in summary["methods"].items()}
    write_yaml(runtime, output, [
        "name 为空时保留编号。可修改 name；status 为名称来源类型。",
        "详细溯源见 " + provenance_output.name + "，插件不加载该文件。",
        "context_* 为上下文摘要，不是官方 DevNote。"])
    write_yaml(provenance, provenance_output, [
        "开发者溯源资料；按 mappings / 分类 / 编号与名称表对应。",
        "sources 保存资源位置、完整条件、脚本调用和补名依据。插件不加载此文件。"])
    return provenance_output


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input-json", type=pathlib.Path, required=True,
                        help="Legacy map parsed to JSON with a standard YAML parser")
    parser.add_argument("--output", type=pathlib.Path, required=True)
    parser.add_argument("--sources-output", type=pathlib.Path)
    args = parser.parse_args()
    document = json.loads(args.input_json.read_text(encoding="utf-8-sig"))
    provenance = write_documents(document, args.output, args.sources_output)
    print(json.dumps({"output": str(args.output), "sources_output": str(provenance)}, ensure_ascii=False))
