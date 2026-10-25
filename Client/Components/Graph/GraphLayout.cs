using System.Globalization;
using System.Runtime.CompilerServices;
using Synapse.Blocks.Graph;

namespace Synapse.Blocks.Components.Graph;

/// <summary>Строка нода: слева вход данных, справа выход (исполнения или данных).</summary>
public sealed record NodeRow(PortDef? Left, PortDef? Right);

/// <summary>
/// Геометрия блока, вычисленная по его описанию. Одна и та же для разметки нода,
/// позиций портов и концов проводов, поэтому провода всегда приходят точно в порт.
///
/// <code>
/// ▶ in   ЗАГОЛОВОК      next ▶    ← шапка: вход исполнения и единственный выход исполнения
///        [настройка]              ← настройки (вид операции, переменная)
/// ● a  [0]            результат ● ← строки портов: входы данных слева, выходы справа
/// ● b  [1]                 да ▶
/// </code>
/// </summary>
public sealed class NodeLayout
{
    public const double Width = 236;
    public const double HeaderHeight = 50;
    public const double ParamHeight = 36;
    public const double RowHeight = 32;
    public const double PaddingBottom = 10;

    private static readonly ConditionalWeakTable<NodeDefinition, NodeLayout> Cache = new();
    private readonly Dictionary<string, (double X, double Y)> _ports = [];

    private NodeLayout(NodeDefinition definition)
    {
        Definition = definition;
        HeaderIn = definition.Inputs.FirstOrDefault(port => port.IsExec);

        var execOutputs = definition.Outputs.Where(port => port.IsExec).ToList();
        // Единственный обычный выход исполнения рисуется в шапке — так цепочка блоков читается как одна линия.
        if (execOutputs.Count == 1 && !execOutputs[0].OpensScope)
        {
            HeaderOut = execOutputs[0];
            execOutputs.Clear();
        }

        var left = definition.Inputs.Where(port => port.IsData).ToList();
        var right = execOutputs.Concat(definition.Outputs.Where(port => port.IsData)).ToList();
        var rows = new List<NodeRow>();
        for (var index = 0; index < Math.Max(left.Count, right.Count); index++)
            rows.Add(new NodeRow(index < left.Count ? left[index] : null, index < right.Count ? right[index] : null));
        Rows = rows;

        ParamsTop = HeaderHeight;
        RowsTop = ParamsTop + definition.Params.Count * ParamHeight;
        Height = RowsTop + rows.Count * RowHeight + PaddingBottom;

        if (HeaderIn is not null) _ports[HeaderIn.Id] = (0, HeaderHeight / 2);
        if (HeaderOut is not null) _ports[HeaderOut.Id] = (Width, HeaderHeight / 2);
        for (var index = 0; index < rows.Count; index++)
        {
            var y = RowsTop + index * RowHeight + RowHeight / 2;
            if (rows[index].Left is { } input) _ports[input.Id] = (0, y);
            if (rows[index].Right is { } output) _ports[output.Id] = (Width, y);
        }
    }

    public static NodeLayout For(NodeDefinition definition) => Cache.GetValue(definition, item => new NodeLayout(item));

    public NodeDefinition Definition { get; }
    public PortDef? HeaderIn { get; }
    public PortDef? HeaderOut { get; }
    public IReadOnlyList<NodeRow> Rows { get; }
    public double ParamsTop { get; }
    public double RowsTop { get; }
    public double Height { get; }

    /// <summary>Центр порта относительно левого верхнего угла нода.</summary>
    public (double X, double Y) PortPosition(string portId)
        => _ports.TryGetValue(portId, out var position) ? position : (Width / 2, HeaderHeight / 2);

    public static string Px(double value) => value.ToString("0.###", CultureInfo.InvariantCulture) + "px";
    public static string Num(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}

/// <summary>Цвета и подписи, общие для палитры, нодов и справки.</summary>
public static class GraphStyle
{
    public static string TypeClass(DataType type) => type switch
    {
        DataType.Number => "dt-number",
        DataType.Text => "dt-text",
        DataType.Bool => "dt-bool",
        DataType.List => "dt-list",
        _ => "dt-any"
    };

    /// <summary>Класс акцента блока: у управляющих блоков свой цвет, у остальных — цвет категории.</summary>
    public static string AccentClass(NodeDefinition definition)
        => $"cat-{definition.Category} type-{definition.TypeId}";

    public static string Kicker(NodeDefinition definition) => definition.IsPure
        ? $"{NodeCategories.Label(definition.Category).ToUpperInvariant()} · ДАННЫЕ"
        : NodeCategories.Label(definition.Category).ToUpperInvariant();

    public static string PortTitle(PortDef port) => port.IsExec
        ? $"{(port.IsInput ? "Вход" : "Выход")} исполнения{(port.Label.Length > 0 ? $" «{port.Label}»" : "")}"
        : $"{(port.IsInput ? "Вход" : "Выход")} «{port.DisplayName}»: {DataTypes.Label(port.Type)}";
}
