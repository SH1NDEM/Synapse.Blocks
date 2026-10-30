using System.Globalization;
using System.Runtime.CompilerServices;
using Synapse.Blocks.Graph;

namespace Synapse.Blocks.Components.Graph;

/// <summary>Узел сетки поля (в клетках).</summary>
public readonly record struct GridPoint(int X, int Y);

/// <summary>Строка портов чипа: слева вход данных, справа выход (исполнения или данных).</summary>
public sealed record NodeRow(PortDef? Left, PortDef? Right);

/// <summary>
/// Геометрия чипа, вычисленная по описанию блока. Все размеры кратны клетке сетки,
/// поэтому разъёмы портов всегда стоят в узлах сетки и схема выглядит аккуратно.
///
/// <code>
///      ┌──────────────────┐
///  ▶━━━┤ МАРКИРОВКА       ├━━━▶   ← шапка: порты порядка
///      │ Название         │
///      │ [настройка]      │       ← настройки, по 2 клетки
///  ●━━━┤ a  [0]   результат├━━━●   ← строки ножек, по 2 клетки
///  ●━━━┤ b  [1]           │
///      └──────────────────┘
/// </code>
/// Разъём порта выступает за корпус на одну клетку: в его внешний конец приходит провод.
/// </summary>
public sealed class NodeLayout
{
    public const double Cell = 16;
    public const double Width = 12 * Cell;
    public const double HeaderHeight = 36;
    public const double ParamHeight = 2 * Cell;
    public const double RowHeight = 2 * Cell;
    public const double PaddingBottom = Cell;
    /// <summary>Точка, куда приходит провод: чуть за краем корпуса, под разъёмом порта (он утоплен в корпус).</summary>
    public const double PortReach = 8;
    /// <summary>Высота портов порядка в шапке (от верха корпуса).</summary>
    public const double HeaderPinY = HeaderHeight / 2;

    private static readonly ConditionalWeakTable<NodeDefinition, NodeLayout> Cache = new();
    private readonly Dictionary<string, (double X, double Y)> _ports = [];

    private NodeLayout(NodeDefinition definition)
    {
        Definition = definition;
        HeaderIn = definition.Inputs.FirstOrDefault(port => port.IsExec);

        var execOutputs = definition.Outputs.Where(port => port.IsExec).ToList();
        // Единственный обычный выход порядка — в шапке, напротив входа: цепочка чипов читается как одна линия.
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

        if (HeaderIn is not null) _ports[HeaderIn.Id] = (-PortReach, HeaderPinY);
        if (HeaderOut is not null) _ports[HeaderOut.Id] = (Width + PortReach, HeaderPinY);
        for (var index = 0; index < rows.Count; index++)
        {
            var y = RowPinY(index);
            if (rows[index].Left is { } input) _ports[input.Id] = (-PortReach, y);
            if (rows[index].Right is { } output) _ports[output.Id] = (Width + PortReach, y);
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
    public int WidthCells => (int)(Width / Cell);
    public int HeightCells => (int)Math.Round(Height / Cell);

    public double RowPinY(int index) => RowsTop + index * RowHeight + RowHeight / 2;

    /// <summary>Внешний конец разъёма порта относительно левого верхнего угла корпуса.</summary>
    public (double X, double Y) PortPosition(string portId)
        => _ports.TryGetValue(portId, out var position) ? position : (Width / 2, HeaderPinY);

    /// <summary>Округляет координату до узла сетки.</summary>
    public static double Snap(double value) => Math.Round(value / Cell) * Cell;

    public static string Px(double value) => value.ToString("0.###", CultureInfo.InvariantCulture) + "px";
    public static string Num(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}

/// <summary>Цвета, подписи и маркировка, общие для палитры, чипов и справки.</summary>
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

    /// <summary>
    /// Маркировка на корпусе, как у настоящей микросхемы: серия, тип и номер в каталоге.
    /// Например, «СН-MATH 11» для «Арифметики».
    /// </summary>
    public static string Marking(NodeDefinition definition, NodeRegistry? registry = null)
    {
        if (definition is CustomNodeDefinition)
            return "СН-USER";
        registry ??= NodeRegistry.Default;
        var index = 0;
        for (var position = 0; position < registry.All.Count; position++)
            if (registry.All[position] == definition) index = position + 1;
        var code = definition.TypeId.Replace("-", "").ToUpperInvariant();
        return $"СН-{(code.Length > 6 ? code[..6] : code)} {index:00}";
    }

    public static string PortTitle(PortDef port) => port.IsExec
        ? $"{(port.IsInput ? "Вход" : "Выход")} порядка{(port.Label.Length > 0 ? $" «{port.Label}»" : "")}"
        : $"{(port.IsInput ? "Вход" : "Выход")} «{port.DisplayName}»: {DataTypes.Label(port.Type)}";
}
