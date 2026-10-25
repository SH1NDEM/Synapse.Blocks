using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Synapse.Blocks.Graph;

public enum ValueKind
{
    Null,
    Bool,
    Number,
    Text,
    List
}

/// <summary>
/// Значение, которое течёт по проводам данных. Неизменяемое, сравнивается по содержимому.
/// Заменяет прежний <c>object?</c>, в котором смешивались JsonNode, double и string.
/// </summary>
public readonly struct Value : IEquatable<Value>
{
    private readonly double _number;
    private readonly string? _text;
    private readonly bool _bool;
    private readonly ImmutableArray<Value> _items;

    private Value(ValueKind kind, double number, string? text, bool boolean, ImmutableArray<Value> items)
    {
        Kind = kind;
        _number = number;
        _text = text;
        _bool = boolean;
        _items = items;
    }

    public ValueKind Kind { get; }

    public static Value Null => default;
    public static Value True => FromBool(true);
    public static Value False => FromBool(false);
    public static Value EmptyText => FromText("");

    public static Value FromNumber(double number) => new(ValueKind.Number, number, null, false, default);
    public static Value FromText(string? text) => new(ValueKind.Text, 0, text ?? "", false, default);
    public static Value FromBool(bool value) => new(ValueKind.Bool, 0, null, value, default);
    public static Value FromList(IEnumerable<Value> items) => new(ValueKind.List, 0, null, false, items.ToImmutableArray());

    public bool IsNull => Kind == ValueKind.Null;
    public bool IsNumber => Kind == ValueKind.Number;
    public bool IsText => Kind == ValueKind.Text;
    public bool IsBool => Kind == ValueKind.Bool;
    public bool IsList => Kind == ValueKind.List;

    /// <summary>Элементы списка; для остальных видов — пустой массив.</summary>
    public ImmutableArray<Value> Items => Kind == ValueKind.List && !_items.IsDefault ? _items : ImmutableArray<Value>.Empty;

    /// <summary>Число напрямую или текст, который можно прочитать как число.</summary>
    public bool TryGetNumber(out double number)
    {
        switch (Kind)
        {
            case ValueKind.Number:
                number = _number;
                return true;
            case ValueKind.Text:
                return TryParseNumber(_text ?? "", out number);
            default:
                number = 0;
                return false;
        }
    }

    public bool TryGetBool(out bool value)
    {
        switch (Kind)
        {
            case ValueKind.Bool:
                value = _bool;
                return true;
            case ValueKind.Text when bool.TryParse((_text ?? "").Trim(), out var parsed):
                value = parsed;
                return true;
            default:
                value = false;
                return false;
        }
    }

    /// <summary>Текстовое представление: то, что увидит игрок и с чем сравниваются тесты.</summary>
    public override string ToString() => Kind switch
    {
        ValueKind.Null => "null",
        ValueKind.Bool => _bool ? "true" : "false",
        ValueKind.Number => FormatNumber(_number),
        ValueKind.Text => _text ?? "",
        ValueKind.List => "[" + string.Join(",", Items.Select(item => item.ToJsonFragment())) + "]",
        _ => ""
    };

    /// <summary>Короткая подпись типа для интерфейса и сообщений об ошибках.</summary>
    public string KindLabel => Kind switch
    {
        ValueKind.Null => "пусто",
        ValueKind.Bool => "логическое",
        ValueKind.Number => "число",
        ValueKind.Text => "текст",
        ValueKind.List => "список",
        _ => "?"
    };

    private string ToJsonFragment() => Kind == ValueKind.Text
        ? "\"" + (_text ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""
        : ToString();

    /// <summary>
    /// Разбирает строку из теста или поля ввода: true/false, число, JSON-массив, строку в кавычках или текст.
    /// </summary>
    public static Value Parse(string? raw)
    {
        var text = (raw ?? "").Trim();
        if (text.Length == 0) return EmptyText;
        if (text == "null") return Null;
        if (bool.TryParse(text, out var boolean)) return FromBool(boolean);
        if (TryParseNumber(text, out var number)) return FromNumber(number);

        if (text[0] == '[' || (text[0] == '"' && text.Length >= 2 && text[^1] == '"'))
        {
            try
            {
                using var document = JsonDocument.Parse(text);
                return FromJson(document.RootElement);
            }
            catch (JsonException)
            {
                // Не JSON — обрабатываем как обычный текст ниже.
            }
        }

        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
            return FromText(text[1..^1]);
        return FromText(text);
    }

    private static Value FromJson(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Array => FromList(element.EnumerateArray().Select(FromJson)),
        JsonValueKind.String => FromText(element.GetString()),
        JsonValueKind.Number => FromNumber(element.GetDouble()),
        JsonValueKind.True => True,
        JsonValueKind.False => False,
        JsonValueKind.Object => FromText(element.GetRawText()),
        _ => Null
    };

    private static bool TryParseNumber(string text, out double number)
        => double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out number)
           && double.IsFinite(number);

    public static string FormatNumber(double number)
    {
        if (Math.Abs(number % 1) < 0.0000001 && Math.Abs(number) < 9e15)
            return ((long)Math.Round(number)).ToString(CultureInfo.InvariantCulture);
        return number.ToString("0.########", CultureInfo.InvariantCulture);
    }

    /// <summary>Сравнение результата программы с ожидаемым ответом теста.</summary>
    public static bool MatchesExpected(Value actual, string expected)
        => string.Equals(Parse(actual.ToString()).ToString(), Parse(expected).ToString(), StringComparison.Ordinal);

    public bool Equals(Value other)
    {
        if (Kind != other.Kind) return false;
        return Kind switch
        {
            ValueKind.Null => true,
            ValueKind.Bool => _bool == other._bool,
            ValueKind.Number => _number.Equals(other._number),
            ValueKind.Text => string.Equals(_text ?? "", other._text ?? "", StringComparison.Ordinal),
            ValueKind.List => Items.SequenceEqual(other.Items),
            _ => false
        };
    }

    public override bool Equals(object? obj) => obj is Value other && Equals(other);

    public override int GetHashCode() => Kind switch
    {
        ValueKind.Bool => HashCode.Combine(Kind, _bool),
        ValueKind.Number => HashCode.Combine(Kind, _number),
        ValueKind.Text => HashCode.Combine(Kind, _text ?? ""),
        ValueKind.List => HashCode.Combine(Kind, Items.Length),
        _ => 0
    };

    public static bool operator ==(Value left, Value right) => left.Equals(right);
    public static bool operator !=(Value left, Value right) => !left.Equals(right);
}

/// <summary>Общие операции над значениями, которые используют разные блоки.</summary>
public static class ValueOps
{
    private const double Epsilon = 0.0000001;

    /// <summary>Нестрогое равенство: числа сравниваются как числа, остальное — как текст без учёта регистра.</summary>
    public static bool LooseEquals(Value left, Value right)
    {
        if (left.TryGetNumber(out var a) && right.TryGetNumber(out var b))
            return Math.Abs(a - b) < Epsilon;
        return string.Equals(left.ToString(), right.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Порядок значений: числа по величине, остальное по алфавиту без учёта регистра.</summary>
    public static int Compare(Value left, Value right)
    {
        if (left.TryGetNumber(out var a) && right.TryGetNumber(out var b))
            return Math.Abs(a - b) < Epsilon ? 0 : a.CompareTo(b);
        return string.Compare(left.ToString(), right.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    public static string Concat(Value left, Value right)
    {
        var builder = new StringBuilder();
        builder.Append(left.ToString());
        builder.Append(right.ToString());
        return builder.ToString();
    }
}
