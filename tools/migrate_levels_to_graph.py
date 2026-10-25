"""Переводит уровни кампании со старых блоков (BlockKind) на блоки двухпроводной модели.

Меняет только поля AllowedBlocks / RequiredBlocks / RequiredLoopCount /
RequiredLoopBodyOperationCount / MaxOperationBlocks, остальной текст файла
(включая встроенные изображения) остаётся байт в байт прежним.

Запуск: python tools/migrate_levels_to_graph.py Server/Data/seed-levels.json [Server/App_Data/levels.json]
"""
import json
import re
import shutil
import sys

# Старый блок → новые блоки, которые закрывают те же задачи.
MAPPING = {
    "Input": ["start"],
    "Output": ["output"],
    # «Операция» умела +,-,*,/, set, append, count.
    "Operation": ["math", "const", "join", "length"],
    # «Условие» умело сравнения, contains/starts/ends и проверку типа.
    "Condition": ["if", "compare", "text-check", "is-type"],
    "Loop": ["repeat"],
    "Variable": ["get-var", "set-var", "change-var"],
    "VariableAction": ["get-var", "set-var", "change-var"],
}
ORDER = ["start", "output", "if", "repeat", "while", "for-each", "get-var", "set-var", "change-var",
         "const", "math", "compare", "logic", "not", "is-type", "text-check", "join", "length", "list-item"]

PATTERN = re.compile(
    r'"AllowedBlocks"\s*:\s*\[(?P<allowed>.*?)\]\s*,(?P<ws>\s*)'
    r'"RequiredBlocks"\s*:\s*\[(?P<required>.*?)\]\s*,\s*'
    r'"RequiredLoopCount"\s*:\s*(?P<loops>[^,\s]+)\s*,\s*'
    r'"RequiredLoopBodyOperationCount"\s*:\s*(?P<body>[^,\s]+)\s*,\s*'
    r'"MaxOperationBlocks"\s*:\s*(?P<max>[^,\s]+)\s*,',
    re.DOTALL)


def strings(raw):
    return json.loads("[" + raw + "]")


def number(raw):
    return None if raw == "null" else int(raw)


def convert(match):
    allowed_old = strings(match["allowed"])
    required_old = strings(match["required"])
    body = number(match["body"])
    max_ops = number(match["max"])

    allowed = set()
    for kind in allowed_old:
        allowed.update(MAPPING.get(kind, []))
    # В модели с проводами данных цикл меняет значение через переменную,
    # поэтому вместе с циклом всегда доступны блоки памяти.
    if "repeat" in allowed:
        allowed.update(MAPPING["Variable"])
    allowed.update(["start", "output"])
    allowed_nodes = [node for node in ORDER if node in allowed]

    rules = []
    if "Condition" in required_old:
        rules.append({"Kind": "MinNodes", "Target": "if", "Count": 1})
    if "Loop" in required_old:
        rules.append({"Kind": "MinNodes", "Target": "repeat", "Count": 1})
    if "Variable" in required_old or "VariableAction" in required_old:
        rules.append({"Kind": "MinNodes", "Target": "category:memory", "Count": 1})
    # Раньше тело цикла состояло из «Операций», теперь — из блоков памяти («Изменить», «Записать»).
    if body:
        rules.append({"Kind": "MinNodesInScope", "Target": "category:memory", "Count": body})
    if max_ops is not None:
        rules.append({"Kind": "MaxNodes", "Target": "math", "Count": max_ops})

    ws = match["ws"]
    nl = "\r\n" if "\r\n" in ws else "\n"
    indent = ws.replace("\r", "").replace("\n", "")
    allowed_json = ", ".join(json.dumps(node) for node in allowed_nodes)
    if rules:
        inner = ("," + nl).join(indent + "  " + json.dumps(rule, ensure_ascii=False) for rule in rules)
        rules_json = "[" + nl + inner + nl + indent + "]"
    else:
        rules_json = "[]"
    return f'"AllowedNodes": [{allowed_json}],{ws}"Rules": {rules_json},'


def migrate(path):
    with open(path, "rb") as file:
        data = file.read()
    bom = data.startswith(b"\xef\xbb\xbf")
    text = data[3:].decode("utf-8") if bom else data.decode("utf-8")
    if '"AllowedNodes"' in text and '"AllowedBlocks"' not in text:
        print(f"{path}: уже в новом формате")
        return
    count = len(json.loads(text))
    migrated, replaced = PATTERN.subn(convert, text)
    if replaced != count:
        raise SystemExit(f"{path}: найдено {replaced} блоков настроек на {count} уровней — файл не изменён")
    json.loads(migrated)  # результат обязан остаться корректным JSON
    shutil.copyfile(path, path + ".pre-graph.bak")
    with open(path, "wb") as file:
        file.write((b"\xef\xbb\xbf" if bom else b"") + migrated.encode("utf-8"))
    print(f"{path}: переведено уровней — {replaced}, копия: {path}.pre-graph.bak")


if __name__ == "__main__":
    for argument in sys.argv[1:]:
        migrate(argument)
