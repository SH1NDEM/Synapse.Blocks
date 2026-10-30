using System.Globalization;
using System.Text;
using Synapse.Blocks.Components.Graph;
using Synapse.Blocks.Graph;

namespace Synapse.Blocks.Components.Tutorial;

/// <summary>
/// Обучающий ролик — «живая гифка» из настоящих чипов игры. Описывается данными: какие чипы стоят
/// на маленьком поле, какие провода протягиваются, куда двигается курсор, что подписано.
/// <see cref="TutorialClipView"/> превращает описание в CSS-анимацию, которая крутится по кругу.
/// Все времена — в секундах от начала ролика, координаты — в пикселях поля ролика.
/// </summary>
public sealed class TutorialClip
{
    public required string Name { get; init; }
    public required string Title { get; init; }
    public double Duration { get; init; } = 6;
    public double Width { get; init; } = 660;
    public double Height { get; init; } = 300;

    public List<ClipChip> Chips { get; } = [];
    public List<ClipWire> Wires { get; } = [];
    public List<ClipPoint> Cursor { get; } = [];
    public List<ClipPulse> Pulses { get; } = [];
    public List<ClipBadge> Badges { get; } = [];
    public List<ClipGlow> Glows { get; } = [];
    public List<ClipCaption> Captions { get; } = [];
    public List<ClipTile> Palette { get; } = [];
}

/// <summary>Чип на поле. Если задан DragFrom, чип «прилетает» из этой точки (из палитры) за время DragStart–DragEnd.</summary>
public sealed record ClipChip(string Key, string Type, double X, double Y)
{
    public double AppearAt { get; init; }
    public Dictionary<string, string> Params { get; init; } = [];
    public (double X, double Y)? DragFrom { get; init; }
    public double DragStart { get; init; }
    public double DragEnd { get; init; }
}

/// <summary>Провод от порта одного чипа к порту другого; прорисовывается за время DrawStart–DrawEnd (0–0 — сразу).</summary>
public sealed record ClipWire(string FromChip, string FromPort, string ToChip, string ToPort, double DrawStart = 0, double DrawEnd = 0);

/// <summary>Точка пути курсора. Click — в этот момент курсор нажимает.</summary>
public sealed record ClipPoint(double T, double X, double Y, bool Click = false);

/// <summary>Значение, бегущее по проводу номер Wire за время Start–End. Label — подпись на бегущей точке.</summary>
public sealed record ClipPulse(int Wire, double Start, double End, string Label = "", string Css = "");

/// <summary>Надпись поверх поля: введённое значение, результат, отметка «тест пройден».</summary>
public sealed record ClipBadge(double Start, double End, double X, double Y, string Text, string Css = "clip-value");

/// <summary>Подсветка чипа, пока он «выполняется».</summary>
public sealed record ClipGlow(string Chip, double Start, double End, string Css = "");

/// <summary>Подпись внизу ролика на время Start–End.</summary>
public sealed record ClipCaption(double Start, double End, string Text);

/// <summary>Плитка палитры слева от поля.</summary>
public sealed record ClipTile(string Type, double Y);

/// <summary>Готовая геометрия и CSS ролика: считается один раз на ролик.</summary>
public sealed class ClipFrame
{
    public required string Css { get; init; }
    public required IReadOnlyList<ClipWireView> Wires { get; init; }
    public required IReadOnlyDictionary<string, NodeDefinition> Definitions { get; init; }
}

public sealed record ClipWireView(int Index, string Path, string Css, double Length);

/// <summary>Строит CSS-анимации ролика.</summary>
public static class ClipAnimator
{
    private const double Fade = .18;

    /// <summary>
    /// Блоки в ролике уменьшены относительно своего центра: места на поле те же,
    /// а проводам между ними просторнее.
    /// </summary>
    public const double ChipScale = .6;
    private static string N(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    public static ClipFrame Build(TutorialClip clip, string prefix, NodeRegistry registry)
    {
        var css = new StringBuilder();
        var duration = clip.Duration;
        string P(double t) => N(Math.Clamp(t / duration * 100, 0, 100)) + "%";
        // Всё исчезает за мгновение до повтора, чтобы ролик начинался заново с чистого поля.
        var end = duration - .35;

        var definitions = new Dictionary<string, NodeDefinition>();
        foreach (var chip in clip.Chips)
            if (registry.TryGet(chip.Type, out var definition))
                definitions[chip.Key] = definition;
        var chipsByKey = clip.Chips.ToDictionary(chip => chip.Key);

        void Animation(string selector, string name)
            => css.Append(selector).Append("{animation:").Append(name).Append(' ').Append(N(duration)).Append("s linear infinite;}");

        // Окно видимости: появиться в start, исчезнуть в finish.
        string Window(double start, double finish)
        {
            var a = Math.Max(0, start);
            var b = Math.Min(end, finish);
            return $"0%{{opacity:0}}{P(Math.Max(0, a - .001))}{{opacity:0}}{P(Math.Min(a + Fade, b))}{{opacity:1}}{P(Math.Max(a, b - Fade))}{{opacity:1}}{P(b)}{{opacity:0}}100%{{opacity:0}}";
        }

        // Чипы: появление или перелёт из палитры.
        for (var index = 0; index < clip.Chips.Count; index++)
        {
            var chip = clip.Chips[index];
            var name = $"{prefix}-chip{index}";
            if (chip.DragFrom is { } from)
            {
                var dx = from.X - chip.X;
                var dy = from.Y - chip.Y;
                css.Append($"@keyframes {name}{{0%{{opacity:0;transform:translate({N(dx)}px,{N(dy)}px) scale(.55)}}");
                css.Append($"{P(chip.DragStart - .001)}{{opacity:0;transform:translate({N(dx)}px,{N(dy)}px) scale(.55)}}");
                css.Append($"{P(chip.DragStart + .05)}{{opacity:.85;transform:translate({N(dx)}px,{N(dy)}px) scale(.6)}}");
                css.Append($"{P(chip.DragEnd)}{{opacity:1;transform:translate(0,0) scale(1)}}");
                css.Append($"{P(end - Fade)}{{opacity:1;transform:translate(0,0) scale(1)}}{P(end)}{{opacity:0;transform:translate(0,0) scale(1)}}100%{{opacity:0}}}}");
            }
            else
            {
                css.Append($"@keyframes {name}{{{Window(chip.AppearAt, end)}}}");
            }
            Animation($".{name}", name);
        }

        // Провода: прорисовываются по длине.
        var wires = new List<ClipWireView>();
        for (var index = 0; index < clip.Wires.Count; index++)
        {
            var wire = clip.Wires[index];
            if (!chipsByKey.TryGetValue(wire.FromChip, out var fromChip) || !chipsByKey.TryGetValue(wire.ToChip, out var toChip)) continue;
            if (!definitions.TryGetValue(fromChip.Key, out var fromDefinition) || !definitions.TryGetValue(toChip.Key, out var toDefinition)) continue;
            var fromPort = fromDefinition.FindPort(wire.FromPort);
            var toPort = toDefinition.FindPort(wire.ToPort);
            if (fromPort is null || toPort is null) continue;

            var start = PortPoint(fromChip, fromDefinition, fromPort.Id);
            var finish = PortPoint(toChip, toDefinition, toPort.Id);
            var (path, length) = Curve(start, finish);
            var kind = fromPort.IsExec
                ? "trace-exec" + fromPort.Id switch { "true" => " trace-true", "false" => " trace-false", _ when fromPort.OpensScope => " trace-scope", _ => "" }
                : $"trace-data {GraphStyle.TypeClass(fromPort.Type == DataType.Any ? toPort.Type : fromPort.Type)}";
            wires.Add(new ClipWireView(index, path, kind, length));

            var name = $"{prefix}-wire{index}";
            var appear = Math.Max(fromChip.DragEnd, Math.Max(fromChip.AppearAt, Math.Max(toChip.AppearAt, toChip.DragEnd)));
            var drawStart = wire.DrawEnd > wire.DrawStart ? wire.DrawStart : appear;
            var drawEnd = wire.DrawEnd > wire.DrawStart ? wire.DrawEnd : appear + .01;
            css.Append($"@keyframes {name}{{0%{{stroke-dashoffset:{N(length)};opacity:0}}{P(drawStart - .001)}{{stroke-dashoffset:{N(length)};opacity:0}}");
            css.Append($"{P(drawStart)}{{stroke-dashoffset:{N(length)};opacity:1}}{P(drawEnd)}{{stroke-dashoffset:0;opacity:1}}");
            css.Append($"{P(end - Fade)}{{stroke-dashoffset:0;opacity:1}}{P(end)}{{stroke-dashoffset:0;opacity:0}}100%{{opacity:0}}}}");
            css.Append($".{name}{{stroke-dasharray:{N(length)} {N(length)};}}");
            Animation($".{name}", name);
        }

        // Бегущие значения: движутся по пути провода.
        for (var index = 0; index < clip.Pulses.Count; index++)
        {
            var pulse = clip.Pulses[index];
            var wire = wires.FirstOrDefault(item => item.Index == pulse.Wire);
            if (wire is null) continue;
            var name = $"{prefix}-pulse{index}";
            css.Append($".{name}{{offset-path:path('{wire.Path}');offset-rotate:0deg;}}");
            css.Append($"@keyframes {name}{{0%{{offset-distance:0%;opacity:0}}{P(pulse.Start - .001)}{{offset-distance:0%;opacity:0}}{P(pulse.Start)}{{offset-distance:0%;opacity:1}}");
            css.Append($"{P(pulse.End)}{{offset-distance:100%;opacity:1}}{P(pulse.End + .15)}{{offset-distance:100%;opacity:0}}100%{{offset-distance:100%;opacity:0}}}}");
            Animation($".{name}", name);
        }

        // Подсветка выполняющихся чипов, надписи и подписи — окна видимости.
        for (var index = 0; index < clip.Glows.Count; index++)
        {
            var glow = clip.Glows[index];
            css.Append($"@keyframes {prefix}-glow{index}{{{Window(glow.Start, glow.End)}}}");
            Animation($".{prefix}-glow{index}", $"{prefix}-glow{index}");
        }
        for (var index = 0; index < clip.Badges.Count; index++)
        {
            var badge = clip.Badges[index];
            css.Append($"@keyframes {prefix}-badge{index}{{{Window(badge.Start, badge.End)}}}");
            Animation($".{prefix}-badge{index}", $"{prefix}-badge{index}");
        }
        for (var index = 0; index < clip.Captions.Count; index++)
        {
            var caption = clip.Captions[index];
            css.Append($"@keyframes {prefix}-cap{index}{{{Window(caption.Start, caption.End)}}}");
            Animation($".{prefix}-cap{index}", $"{prefix}-cap{index}");
        }

        // Курсор: двигается по точкам, в точках с нажатием — «вжимается», а вокруг расходится круг.
        if (clip.Cursor.Count > 0)
        {
            var points = clip.Cursor.OrderBy(point => point.T)
                .Select(point => { var (x, y) = MapPoint(clip, definitions, point.X, point.Y, 14); return point with { X = x, Y = y }; })
                .ToList();
            css.Append($"@keyframes {prefix}-cursor{{");
            css.Append($"0%{{transform:translate({N(points[0].X)}px,{N(points[0].Y)}px);opacity:0}}");
            css.Append($"{P(Math.Max(.01, points[0].T - .2))}{{transform:translate({N(points[0].X)}px,{N(points[0].Y)}px);opacity:1}}");
            foreach (var point in points)
                css.Append($"{P(point.T)}{{transform:translate({N(point.X)}px,{N(point.Y)}px);opacity:1;animation-timing-function:ease-in-out}}");
            var last = points[^1];
            css.Append($"{P(end)}{{transform:translate({N(last.X)}px,{N(last.Y)}px);opacity:0}}100%{{transform:translate({N(last.X)}px,{N(last.Y)}px);opacity:0}}}}");
            Animation($".{prefix}-cursor", $"{prefix}-cursor");

            var clicks = points.Where(point => point.Click).ToList();
            for (var index = 0; index < clicks.Count; index++)
            {
                var click = clicks[index];
                css.Append($"@keyframes {prefix}-click{index}{{0%{{opacity:0;transform:translate({N(click.X)}px,{N(click.Y)}px) scale(.2)}}");
                css.Append($"{P(click.T - .001)}{{opacity:0;transform:translate({N(click.X)}px,{N(click.Y)}px) scale(.2)}}");
                css.Append($"{P(click.T)}{{opacity:.9;transform:translate({N(click.X)}px,{N(click.Y)}px) scale(.3)}}");
                css.Append($"{P(click.T + .45)}{{opacity:0;transform:translate({N(click.X)}px,{N(click.Y)}px) scale(1.6)}}100%{{opacity:0}}}}");
                Animation($".{prefix}-click{index}", $"{prefix}-click{index}");
            }
        }

        return new ClipFrame { Css = css.ToString(), Wires = wires, Definitions = definitions };
    }

    public static int ClickCount(TutorialClip clip) => clip.Cursor.Count(point => point.Click);

    /// <summary>Точка порта уменьшенного блока.</summary>
    public static (double X, double Y) PortPoint(ClipChip chip, NodeDefinition definition, string portId)
    {
        var (x, y) = NodeLayout.For(definition).PortPosition(portId);
        return Shrink(chip, definition, chip.X + x, chip.Y + y);
    }

    /// <summary>Прямоугольник уменьшенного блока на поле ролика.</summary>
    public static (double X, double Y, double Width, double Height) ScaledRect(ClipChip chip, NodeDefinition definition)
    {
        var height = NodeLayout.For(definition).Height;
        var width = NodeLayout.Width;
        return (chip.X + width * (1 - ChipScale) / 2, chip.Y + height * (1 - ChipScale) / 2, width * ChipScale, height * ChipScale);
    }

    /// <summary>Сдвигает точку к центру блока так же, как уменьшается сам блок.</summary>
    private static (double X, double Y) Shrink(ClipChip chip, NodeDefinition definition, double x, double y)
    {
        var centerX = chip.X + NodeLayout.Width / 2;
        var centerY = chip.Y + NodeLayout.For(definition).Height / 2;
        return (centerX + (x - centerX) * ChipScale, centerY + (y - centerY) * ChipScale);
    }

    /// <summary>
    /// Точки курсора и надписей задаются по полноразмерным блокам. Если точка лежит на блоке
    /// (с запасом margin), она переносится на уменьшенный блок; остальные точки не меняются.
    /// </summary>
    public static (double X, double Y) MapPoint(TutorialClip clip, IReadOnlyDictionary<string, NodeDefinition> definitions, double x, double y, double margin)
    {
        foreach (var chip in clip.Chips)
        {
            if (!definitions.TryGetValue(chip.Key, out var definition)) continue;
            var height = NodeLayout.For(definition).Height;
            if (x >= chip.X - margin && x <= chip.X + NodeLayout.Width + margin && y >= chip.Y - margin && y <= chip.Y + height + margin)
                return Shrink(chip, definition, x, y);
        }
        return (x, y);
    }

    /// <summary>Та же кривая Безье, что у проводов на поле, и её примерная длина.</summary>
    private static (string Path, double Length) Curve((double X, double Y) from, (double X, double Y) to)
    {
        var dx = to.X - from.X;
        var pull = dx >= 0 ? Math.Clamp(dx * .45, 16, 140) : Math.Clamp(60 + Math.Abs(to.Y - from.Y) * .2, 60, 160);
        var c1 = (X: from.X + pull, Y: from.Y);
        var c2 = (X: to.X - pull, Y: to.Y);
        var path = FormattableString.Invariant($"M {from.X:0.##} {from.Y:0.##} C {c1.X:0.##} {c1.Y:0.##}, {c2.X:0.##} {c2.Y:0.##}, {to.X:0.##} {to.Y:0.##}");
        var length = 0.0;
        var previous = from;
        for (var index = 1; index <= 40; index++)
        {
            var t = index / 40.0;
            var u = 1 - t;
            var point = (
                X: u * u * u * from.X + 3 * u * u * t * c1.X + 3 * u * t * t * c2.X + t * t * t * to.X,
                Y: u * u * u * from.Y + 3 * u * u * t * c1.Y + 3 * u * t * t * c2.Y + t * t * t * to.Y);
            length += Math.Sqrt((point.X - previous.X) * (point.X - previous.X) + (point.Y - previous.Y) * (point.Y - previous.Y));
            previous = point;
        }
        return (path, Math.Ceiling(length + 2));
    }
}
