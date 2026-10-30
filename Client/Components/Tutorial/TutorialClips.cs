namespace Synapse.Blocks.Components.Tutorial;

/// <summary>
/// Готовые обучающие ролики. В шаге вступления уровня ролик вставляется строкой <c>clip:имя</c>
/// в поле картинки (вместо ссылки на GIF).
/// </summary>
public static class TutorialClips
{
    public const string Prefix = "clip:";

    private static readonly Dictionary<string, Func<TutorialClip>> Builders = new(StringComparer.OrdinalIgnoreCase)
    {
        ["palette"] = Palette,
        ["exec-wire"] = ExecWire,
        ["data-wire"] = DataWire,
        ["run"] = Run,
        ["math"] = MathChain,
        ["if"] = IfBranch,
        ["loop"] = Loop,
        ["memory"] = Memory,
        ["type-check"] = TypeCheck,
    };

    private static readonly Dictionary<string, TutorialClip> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Ролики для выбора в редакторе уровней: имя и название.</summary>
    public static IReadOnlyList<(string Name, string Title)> All { get; } = Builders.Select(pair => (pair.Key, pair.Value().Title)).ToList();

    /// <summary>Ролик по имени (с префиксом <c>clip:</c> или без него) или null.</summary>
    public static TutorialClip? Find(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var key = name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) ? name[Prefix.Length..] : name;
        if (Cache.TryGetValue(key, out var cached)) return cached;
        return Builders.TryGetValue(key, out var build) ? Cache[key] = build() : null;
    }

    public static bool IsClip(string? mediaUrl) => mediaUrl?.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) == true;

    private static Dictionary<string, string> P(params (string Key, string Value)[] values) => values.ToDictionary(pair => pair.Key, pair => pair.Value);

    // ───────────── Палитра: блок перетаскивается на поле ─────────────
    private static TutorialClip Palette()
    {
        var clip = new TutorialClip { Name = "palette", Title = "Блоки из палитры", Duration = 6 };
        clip.Palette.AddRange([new("start", 40), new("math", 80), new("output", 120), new("if-compare", 160)]);
        clip.Chips.Add(new ClipChip("in", "start", 20, 30));
        clip.Chips.Add(new ClipChip("out", "output", 316, 30));
        clip.Chips.Add(new ClipChip("math", "math", 170, 140) { DragFrom = (498, 20), DragStart = 1.1, DragEnd = 2.3 });
        clip.Wires.Add(new ClipWire("in", "next", "out", "in"));
        clip.Cursor.AddRange([new(.2, 300, 270), new(1, 594, 94, Click: true), new(2.3, 266, 214), new(3.1, 120, 280)]);
        clip.Captions.AddRange([new(.1, 1.1, "Блоки лежат в палитре справа"), new(1.1, 2.6, "Перетащи блок на поле — или просто нажми на него"), new(2.6, 5.6, "Блок на поле. Теперь его можно соединять проводами")]);
        return clip;
    }

    // ───────────── Провод порядка: «Вход» → «Выход» ─────────────
    private static TutorialClip ExecWire()
    {
        var clip = new TutorialClip { Name = "exec-wire", Title = "Провод порядка", Duration = 6, Width = 600, Height = 230 };
        clip.Chips.Add(new ClipChip("in", "start", 40, 70));
        clip.Chips.Add(new ClipChip("out", "output", 360, 70));
        clip.Wires.Add(new ClipWire("in", "next", "out", "in", 1.25, 2.4));
        clip.Cursor.AddRange([new(.3, 300, 200), new(1.2, 240, 88, Click: true), new(2.4, 352, 88), new(3.2, 430, 200)]);
        clip.Pulses.Add(new ClipPulse(0, 2.8, 3.5, Css: "pulse-exec"));
        clip.Glows.Add(new ClipGlow("in", 2.5, 2.9));
        clip.Glows.Add(new ClipGlow("out", 3.5, 4.6));
        clip.Captions.AddRange([new(.1, 1.3, "Белые разъёмы задают порядок: что выполнить следом"), new(1.3, 2.7, "Тяни провод от белого выхода к белому входу"), new(2.7, 5.6, "Готово: блоки выполнятся друг за другом")]);
        return clip;
    }

    // ───────────── Провод данных ─────────────
    private static TutorialClip DataWire()
    {
        var clip = new TutorialClip { Name = "data-wire", Title = "Провод данных", Duration = 6.2, Width = 600, Height = 230 };
        clip.Chips.Add(new ClipChip("in", "start", 40, 70));
        clip.Chips.Add(new ClipChip("out", "output", 360, 70));
        clip.Wires.Add(new ClipWire("in", "next", "out", "in"));
        clip.Wires.Add(new ClipWire("in", "value", "out", "value", 1.15, 2.3));
        clip.Cursor.AddRange([new(.3, 300, 210), new(1.1, 240, 122, Click: true), new(2.3, 352, 122), new(3, 430, 210)]);
        clip.Badges.Add(new ClipBadge(2.4, 3.8, 70, 36, "вход: 6", "clip-input"));
        clip.Pulses.Add(new ClipPulse(1, 2.7, 3.6, "6", "pulse-data"));
        clip.Glows.Add(new ClipGlow("out", 3.6, 4.4));
        clip.Badges.Add(new ClipBadge(3.7, 5.8, 380, 162, "результат: 6", "clip-ok"));
        clip.Captions.AddRange([new(.1, 1.2, "Цветные разъёмы передают данные"), new(1.2, 2.6, "Соедини цветные разъёмы проводом"), new(2.6, 5.8, "Число 6 дошло до конца — это ответ программы")]);
        return clip;
    }

    // ───────────── Запуск тестов ─────────────
    private static TutorialClip Run()
    {
        var clip = new TutorialClip { Name = "run", Title = "Запуск тестов", Duration = 6.6, Width = 600, Height = 230 };
        clip.Chips.Add(new ClipChip("in", "start", 40, 30));
        clip.Chips.Add(new ClipChip("out", "output", 360, 30));
        clip.Wires.Add(new ClipWire("in", "next", "out", "in"));
        clip.Wires.Add(new ClipWire("in", "value", "out", "value"));
        clip.Badges.Add(new ClipBadge(0, 99, 40, 168, "▶  Запустить все тесты", "clip-button"));
        clip.Cursor.AddRange([new(.3, 320, 210), new(1, 150, 184, Click: true), new(1.6, 230, 222)]);
        string[] inputs = ["6", "15", "-4"];
        for (var index = 0; index < inputs.Length; index++)
        {
            var start = 1.4 + index * 1.4;
            clip.Glows.Add(new ClipGlow("in", start, start + .45));
            clip.Pulses.Add(new ClipPulse(1, start + .2, start + .85, inputs[index], "pulse-data"));
            clip.Glows.Add(new ClipGlow("out", start + .8, start + 1.2));
            clip.Badges.Add(new ClipBadge(start + 1.05, 99, 300 + index * 98, 170, $"✓ {inputs[index]} → {inputs[index]}", "clip-test"));
        }
        clip.Captions.AddRange([new(.1, 1.3, "Нажми «Запустить все тесты»"), new(1.3, 4.6, "Каждый тест запускает схему с новыми входными данными"), new(4.6, 6.2, "Все галочки зелёные — система восстановлена!")]);
        return clip;
    }

    // ───────────── Арифметика между «Входом» и «Выходом» ─────────────
    private static TutorialClip MathChain()
    {
        var clip = new TutorialClip { Name = "math", Title = "Блок «Арифметика»", Duration = 8 };
        clip.Chips.Add(new ClipChip("in", "start", 16, 30));
        clip.Chips.Add(new ClipChip("out", "output", 448, 30));
        clip.Chips.Add(new ClipChip("math", "math", 232, 120) { Params = P(("op", "+"), ("b", "1")) });
        clip.Wires.Add(new ClipWire("in", "next", "out", "in"));
        clip.Wires.Add(new ClipWire("in", "value", "math", "a", .65, 1.4));
        clip.Wires.Add(new ClipWire("math", "result", "out", "value", 3.45, 4.2));
        clip.Cursor.AddRange([new(.2, 330, 280), new(.6, 216, 82, Click: true), new(1.4, 224, 204), new(2, 372, 172, Click: true), new(2.8, 300, 236, Click: true), new(3.4, 432, 204, Click: true), new(4.2, 440, 82), new(4.8, 560, 250)]);
        clip.Badges.Add(new ClipBadge(2.1, 99, 306, 158, "×", "clip-field clip-select"));
        clip.Badges.Add(new ClipBadge(2.9, 99, 262, 226, "2", "clip-field"));
        clip.Pulses.Add(new ClipPulse(1, 4.6, 5.2, "6", "pulse-data"));
        clip.Glows.Add(new ClipGlow("math", 5.1, 5.6));
        clip.Pulses.Add(new ClipPulse(2, 5.5, 6.1, "12", "pulse-data"));
        clip.Badges.Add(new ClipBadge(6.1, 7.6, 470, 122, "результат: 12", "clip-ok"));
        clip.Captions.AddRange([new(.1, 1.7, "Подай число на вход блока"), new(1.7, 3.3, "Настрой блок: что сделать с числом"), new(3.3, 4.5, "Результат веди дальше"), new(4.5, 7.6, "6 × 2 = 12 — сигнал усилен!")]);
        return clip;
    }

    // ───────────── «Если»: две ветки ─────────────
    private static TutorialClip IfBranch()
    {
        var clip = new TutorialClip { Name = "if", Title = "Блок «Если»", Duration = 7.6 };
        clip.Chips.Add(new ClipChip("in", "start", 16, 100));
        clip.Chips.Add(new ClipChip("if", "if-compare", 232, 70) { Params = P(("op", "<"), ("b", "0")) });
        clip.Chips.Add(new ClipChip("yes", "output", 460, 10));
        clip.Chips.Add(new ClipChip("no", "output", 460, 190));
        clip.Wires.Add(new ClipWire("in", "next", "if", "in", .3, .7));
        clip.Wires.Add(new ClipWire("if", "true", "yes", "in", .7, 1.1));
        clip.Wires.Add(new ClipWire("if", "false", "no", "in", 1.1, 1.5));
        clip.Wires.Add(new ClipWire("in", "value", "if", "a", .3, .9));
        // Первый запуск: −5 < 0 — ветка «да».
        clip.Badges.Add(new ClipBadge(1.7, 3.7, 30, 70, "вход: −5", "clip-input"));
        clip.Pulses.Add(new ClipPulse(3, 1.9, 2.5, "−5", "pulse-data"));
        clip.Glows.Add(new ClipGlow("if", 2.5, 3.2));
        clip.Badges.Add(new ClipBadge(2.6, 3.8, 252, 40, "−5 < 0 → да", "clip-ok"));
        clip.Pulses.Add(new ClipPulse(1, 3.0, 3.6, Css: "pulse-true"));
        clip.Glows.Add(new ClipGlow("yes", 3.6, 4.2));
        // Второй запуск: 7 < 0 — неверно, ветка «нет».
        clip.Badges.Add(new ClipBadge(4.3, 6.4, 30, 70, "вход: 7", "clip-input"));
        clip.Pulses.Add(new ClipPulse(3, 4.5, 5.1, "7", "pulse-data"));
        clip.Glows.Add(new ClipGlow("if", 5.1, 5.8));
        clip.Badges.Add(new ClipBadge(5.2, 6.5, 252, 40, "7 < 0 → нет", "clip-no"));
        clip.Pulses.Add(new ClipPulse(2, 5.6, 6.2, Css: "pulse-false"));
        clip.Glows.Add(new ClipGlow("no", 6.2, 6.9));
        clip.Captions.AddRange([new(.1, 1.7, "Развилка проверяет условие"), new(1.7, 4.2, "Верно — выполнение идёт по ветке «да»"), new(4.2, 7.2, "Неверно — по ветке «нет». В каждой ветке свой путь")]);
        return clip;
    }

    // ───────────── «Повторить»: тело несколько раз ─────────────
    private static TutorialClip Loop()
    {
        var clip = new TutorialClip { Name = "loop", Title = "Блок «Повторить»", Duration = 8.2 };
        clip.Chips.Add(new ClipChip("loop", "repeat", 30, 70) { Params = P(("count", "3")) });
        clip.Chips.Add(new ClipChip("change", "change-var", 300, 10) { Params = P(("variable", "A"), ("op", "mul"), ("value", "2")) });
        clip.Chips.Add(new ClipChip("out", "output", 300, 200));
        clip.Wires.Add(new ClipWire("loop", "body", "change", "in", .3, .9));
        clip.Wires.Add(new ClipWire("loop", "done", "out", "in", .9, 1.4));
        clip.Badges.Add(new ClipBadge(1, 2.5, 515, 70, "A = 1", "clip-memory"));
        string[] values = ["2", "4", "8"];
        for (var round = 0; round < 3; round++)
        {
            var start = 1.6 + round * 1.5;
            clip.Badges.Add(new ClipBadge(start, start + 1.45, 40, 40, $"повтор {round + 1} из 3", "clip-input"));
            clip.Pulses.Add(new ClipPulse(0, start, start + .6, Css: "pulse-scope"));
            clip.Glows.Add(new ClipGlow("change", start + .6, start + 1.2));
            clip.Badges.Add(new ClipBadge(start + .9, round == 2 ? 7.8 : start + 2.4, 515, 70, $"A = {values[round]}", "clip-memory"));
        }
        clip.Pulses.Add(new ClipPulse(1, 6.2, 6.8, Css: "pulse-exec"));
        clip.Glows.Add(new ClipGlow("out", 6.8, 7.6));
        clip.Captions.AddRange([new(.1, 1.6, "Цикл выполняет свою ветку несколько раз"), new(1.6, 6.1, "Конец ветки пустой — цикл сам начинает новый круг"), new(6.1, 7.8, "После 3 кругов программа идёт дальше")]);
        return clip;
    }

    // ───────────── Память: «Записать» и «Переменная» ─────────────
    private static TutorialClip Memory()
    {
        var clip = new TutorialClip { Name = "memory", Title = "Переменные", Duration = 7.2 };
        clip.Chips.Add(new ClipChip("in", "start", 16, 30));
        clip.Chips.Add(new ClipChip("set", "set-var", 232, 30) { Params = P(("variable", "A")) });
        clip.Chips.Add(new ClipChip("out", "output", 448, 150));
        clip.Chips.Add(new ClipChip("get", "get-var", 232, 170) { Params = P(("variable", "A")) });
        clip.Wires.Add(new ClipWire("in", "next", "set", "in", .3, .6));
        clip.Wires.Add(new ClipWire("set", "next", "out", "in", .6, .9));
        clip.Wires.Add(new ClipWire("in", "value", "set", "value", .9, 1.2));
        clip.Wires.Add(new ClipWire("get", "value", "out", "value", 1.2, 1.5));
        clip.Badges.Add(new ClipBadge(.4, 3.0, 462, 50, "память: A = 0", "clip-memory"));
        clip.Badges.Add(new ClipBadge(1.8, 3.6, 30, 4, "вход: 6", "clip-input"));
        clip.Pulses.Add(new ClipPulse(2, 2.0, 2.6, "6", "pulse-data"));
        clip.Glows.Add(new ClipGlow("set", 2.6, 3.2));
        clip.Badges.Add(new ClipBadge(3.0, 6.9, 462, 50, "память: A = 6", "clip-memory clip-memory-hot"));
        clip.Pulses.Add(new ClipPulse(1, 3.3, 3.9, Css: "pulse-exec"));
        clip.Pulses.Add(new ClipPulse(3, 3.9, 4.5, "6", "pulse-data"));
        clip.Glows.Add(new ClipGlow("out", 4.4, 5.0));
        clip.Badges.Add(new ClipBadge(4.6, 6.8, 470, 246, "результат: 6", "clip-ok"));
        clip.Captions.AddRange([new(.1, 2.0, "Один блок кладёт значение в память"), new(2.0, 3.8, "Переменная хранит его, пока программа работает"), new(3.8, 6.8, "Другой блок достаёт его, когда нужно")]);
        return clip;
    }

    // ───────────── «Проверка типа» и «Условие» ─────────────
    private static TutorialClip TypeCheck()
    {
        var clip = new TutorialClip { Name = "type-check", Title = "Число или текст?", Duration = 8 };
        clip.Chips.Add(new ClipChip("in", "start", 16, 120));
        clip.Chips.Add(new ClipChip("check", "is-type", 232, 10) { Params = P(("type", "number")) });
        clip.Chips.Add(new ClipChip("if", "if", 232, 160));
        clip.Chips.Add(new ClipChip("yes", "output", 460, 60));
        clip.Chips.Add(new ClipChip("no", "output", 460, 200));
        clip.Wires.Add(new ClipWire("in", "next", "if", "in", .3, .6));
        clip.Wires.Add(new ClipWire("if", "true", "yes", "in", .6, .9));
        clip.Wires.Add(new ClipWire("if", "false", "no", "in", .9, 1.2));
        clip.Wires.Add(new ClipWire("in", "value", "check", "value", 1.2, 1.5));
        clip.Wires.Add(new ClipWire("check", "result", "if", "condition", 1.5, 1.8));
        // Число — ветка «да».
        clip.Badges.Add(new ClipBadge(2.0, 4.0, 24, 92, "вход: 42", "clip-input"));
        clip.Pulses.Add(new ClipPulse(3, 2.1, 2.6, "42", "pulse-data"));
        clip.Glows.Add(new ClipGlow("check", 2.6, 3.0));
        clip.Pulses.Add(new ClipPulse(4, 3.0, 3.5, "да", "pulse-bool"));
        clip.Pulses.Add(new ClipPulse(1, 3.5, 4.0, Css: "pulse-true"));
        clip.Glows.Add(new ClipGlow("yes", 4.0, 4.6));
        // Текст — ветка «нет».
        clip.Badges.Add(new ClipBadge(4.8, 6.8, 24, 92, "вход: привет", "clip-input"));
        clip.Pulses.Add(new ClipPulse(3, 4.9, 5.4, "привет", "pulse-data pulse-text"));
        clip.Glows.Add(new ClipGlow("check", 5.4, 5.8));
        clip.Pulses.Add(new ClipPulse(4, 5.8, 6.3, "нет", "pulse-bool"));
        clip.Pulses.Add(new ClipPulse(2, 6.3, 6.8, Css: "pulse-false"));
        clip.Glows.Add(new ClipGlow("no", 6.8, 7.4));
        clip.Captions.AddRange([new(.1, 2.0, "Проверка отвечает «да» или «нет»: это число?"), new(2.0, 4.7, "Ответ выбирает, по какой ветке идти"), new(4.7, 7.6, "Текст — не число, поэтому ветка «нет»")]);
        return clip;
    }
}
