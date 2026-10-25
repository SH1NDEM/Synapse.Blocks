using System.Diagnostics.CodeAnalysis;
using Synapse.Blocks.Graph.Nodes;

namespace Synapse.Blocks.Graph;

/// <summary>
/// Каталог видов блоков. Чтобы добавить блок, достаточно написать класс-наследник
/// <see cref="NodeDefinition"/> и зарегистрировать его — интерпретатор, валидатор
/// и (на следующем этапе) редактор подхватят его сами.
/// </summary>
public sealed class NodeRegistry
{
    private readonly Dictionary<string, NodeDefinition> _definitions = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<NodeDefinition> _ordered = [];

    /// <summary>Реестр со всеми встроенными блоками.</summary>
    public static NodeRegistry Default { get; } = CreateDefault();

    public static NodeRegistry CreateDefault()
    {
        var registry = new NodeRegistry();
        BuiltInNodes.RegisterAll(registry);
        return registry;
    }

    /// <summary>Блоки в порядке регистрации — в этом порядке их показывает палитра.</summary>
    public IReadOnlyList<NodeDefinition> All => _ordered;

    public NodeRegistry Register(NodeDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.TypeId))
            throw new ArgumentException("У блока должен быть TypeId.", nameof(definition));
        if (_definitions.ContainsKey(definition.TypeId))
            throw new ArgumentException($"Блок «{definition.TypeId}» уже зарегистрирован.", nameof(definition));

        // Порты ищутся по Id, поэтому он уникален среди всех портов блока.
        var portIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var port in definition.Ports)
            if (!portIds.Add(port.Id))
                throw new ArgumentException($"У блока «{definition.TypeId}» повторяется Id порта «{port.Id}».", nameof(definition));

        // Значения входов данных и настройки хранятся в одном словаре GraphNode.Params,
        // поэтому их Id не должны пересекаться. Выходы в словарь не попадают.
        var storedIds = definition.Ports.Where(port => port.IsData && port.IsInput).Select(port => port.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var param in definition.Params)
            if (!storedIds.Add(param.Id))
                throw new ArgumentException($"У блока «{definition.TypeId}» Id настройки «{param.Id}» совпадает с Id входа или другой настройки.", nameof(definition));

        _definitions[definition.TypeId] = definition;
        _ordered.Add(definition);
        return this;
    }

    public bool TryGet(string typeId, [NotNullWhen(true)] out NodeDefinition? definition)
        => _definitions.TryGetValue(typeId ?? "", out definition);

    public NodeDefinition Get(string typeId)
        => TryGet(typeId, out var definition) ? definition : throw new KeyNotFoundException($"Неизвестный блок «{typeId}».");

    public IEnumerable<NodeDefinition> InCategory(string category) => _ordered.Where(definition => definition.Category == category);
}
