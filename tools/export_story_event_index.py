"""Export read-only event conditions from the installed game, never gameplay state.

Requires UnityPy, like export_code_names.py. Pass --python-dir when using a
workspace-local UnityPy installation. Runtime uses JSON and native value getters.
"""
import argparse
import hashlib
import json
import pathlib
import sys

parser = argparse.ArgumentParser()
parser.add_argument('--game', type=pathlib.Path, required=True)
parser.add_argument('--output', type=pathlib.Path, required=True)
parser.add_argument('--python-dir', type=pathlib.Path)
args = parser.parse_args()
if args.python_dir:
    sys.path.insert(0, str(args.python_dir))
import UnityPy
from export_code_names import Reader

root = args.game / 'Mortal_Data'
objects, classes = {}, {}
for path in sorted(root.glob('*.assets')):
    env = UnityPy.load(str(path))
    scripts = {}
    for obj in env.objects:
        if obj.type.name != 'MonoBehaviour':
            continue
        asset = obj.read(check_read=False)
        key = asset.m_Script.m_FileID, asset.m_Script.m_PathID
        if key not in scripts:
            scripts[key] = asset.m_Script.deref().read().m_ClassName
        pointer = path.name.lower(), obj.path_id
        objects[pointer], classes[pointer] = obj, scripts[key]

def ident(pointer):
    return None if pointer is None else f'{pointer[0]}:{pointer[1]}'

def name(pointer):
    return Reader(objects[pointer]).name if pointer in objects else None

def finish(rd):
    if rd.pos != len(rd.raw):
        raise ValueError(f'Unrecognized {classes[(pathlib.Path(rd.obj.assets_file.name).name.lower(), rd.obj.path_id)]} layout: {rd.name}')

def condition_item(rd):
    rd.string()  # developer note
    logic = rd.integer()
    comparisons = []
    for _ in range(rd.integer()):
        comparisons.append({'Op': rd.integer(), 'Left': ident(rd.pointer()), 'Right': ident(rd.pointer())})
    return {'Logic': logic, 'Comparisons': comparisons}

groups, conditions, switches, events, pools = {}, {}, {}, {}, {}
field_map = {1: ('_stat', 0), 2: ('_relationship', 1), 3: ('_flag', 2),
             4: ('_book', 3), 5: ('_misc', 4), 6: ('_specialItem', 5),
             9: ('_gameTime', 8), 10: ('_talentData', 6), 11: ('_book', 3),
             12: ('_upgradeItemData', 9), 13: ('_upgradeItemData', 9),
             14: ('_bookCollection', 10), 15: ('_libraryAchieve', 11)}
for pointer, cls in classes.items():
    if cls not in ('StatGroupVariable', 'ConditionResultData', 'SwitchResultData', 'PositionEventData', 'PositionResultData'):
        continue
    rd = Reader(objects[pointer])
    if cls == 'StatGroupVariable':
        operation, values = rd.integer(), []
        for _ in range(rd.integer()):
            kind, constant = rd.integer(), rd.integer()
            pointers = [rd.pointer() for _ in range(3)]
            rd.integer(); rd.integer()  # random bounds; random values remain unknown
            pointers += [rd.pointer() for _ in range(5)]
            negative = rd.boolean()
            pointers += [rd.pointer() for _ in range(5)]
            value = {'Kind': kind, 'Constant': constant, 'Negative': negative}
            if kind == 8:
                value['Group'] = ident(pointers[7])
            elif kind == 16:
                value['Condition'] = name(pointers[12])
            elif kind in field_map:
                field, offset = field_map[kind]
                value.update(Field=field, Target=name(pointers[offset]))
            elif kind not in (0, 7):
                raise ValueError('Unsupported value kind: ' + str(kind))
            values.append(value)
        groups[ident(pointer)] = {'Operation': operation, 'Values': values}
    elif cls in ('ConditionResultData', 'SwitchResultData'):
        rd.string(); rd.integer()  # note and serialized execution count, not runtime state
        counted = rd.boolean() if cls == 'SwitchResultData' else False
        items = [condition_item(rd) for _ in range(rd.integer())]
        if cls == 'SwitchResultData':
            for _ in range(rd.integer()):
                rd.integer(); rd.integer()
            switches[rd.name] = {'Counted': counted, 'Items': items}
        else:
            conditions[rd.name] = items
    elif cls == 'PositionEventData':
        note, mapping = rd.string(), rd.pointer()
        enabled = rd.boolean()
        active = condition_item(rd)
        rate = rd.integer()
        rates = []
        for _ in range(rd.integer()):
            item = condition_item(rd)
            rates.append({'Condition': item, 'Rate': rd.integer()})
        events[ident(pointer)] = {'Code': rd.name, 'Note': note, 'NativeTitle': name(mapping),
                                 'Conditional': enabled, 'Active': active, 'Rate': rate, 'Rates': rates}
    else:
        entries = [ident(rd.pointer()) for _ in range(rd.integer())]
        rd.string(); rd.string()  # note, script prefix; no story execution needed
        pools[rd.name] = entries
    finish(rd)

# Keep the condition/group closure; omit unrelated stat expression assets.
used_groups, used_conditions = set(), set(conditions)
def visit_items(items):
    for item in items:
        for compare in item['Comparisons']:
            visit_group(compare['Left']); visit_group(compare['Right'])

def visit_group(key):
    if key is None or key in used_groups:
        return
    if key not in groups:
        raise ValueError('Missing expression ' + key)
    used_groups.add(key)
    for value in groups[key]['Values']:
        if value['Kind'] == 8:
            visit_group(value['Group'])
        elif value['Kind'] == 16:
            used_conditions.add(value['Condition'])

for items in conditions.values():
    visit_items(items)
for switch in switches.values():
    visit_items(switch['Items'])
for event in events.values():
    visit_items([event['Active']] + [x['Condition'] for x in event['Rates']])

settings = None
env = UnityPy.load(str(root / 'globalgamemanagers'))
for obj in env.objects:
    if obj.type.name == 'PlayerSettings':
        settings = obj.read_typetree()
        break
if not settings or not settings.get('bundleVersion'):
    raise ValueError('Game version is unavailable')
files = sorted({key.split(':', 1)[0] for key in used_groups}
               | {pointer[0] for pointer, cls in classes.items() if cls in (
                   'ConditionResultData', 'SwitchResultData', 'PositionEventData', 'PositionResultData')}
               | {'Managed/Mortal.Core.dll', 'Managed/Mortal.Story.dll'})
index = {'Version': 1, 'GameVersion': settings['bundleVersion'],
         'Hashes': {path: hashlib.sha256((root / path).read_bytes()).hexdigest() for path in files},
         'Groups': {key: groups[key] for key in sorted(used_groups)},
         'Conditions': conditions, 'Switches': switches,
         'Pools': {key: [events[event] for event in values] for key, values in pools.items()}}
args.output.parent.mkdir(parents=True, exist_ok=True)
args.output.write_text(json.dumps(index, ensure_ascii=False, separators=(',', ':')), encoding='utf8')
print(json.dumps({'version': index['GameVersion'], 'groups': len(used_groups), 'conditions': len(conditions),
                  'switches': len(switches), 'pools': len(pools), 'bytes': args.output.stat().st_size}))
