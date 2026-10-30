namespace Synapse.Blocks.Graph.Nodes;

// ─────────────────── Служебные чипы внутри своего блока ───────────────────
// Порты зависят от входов и выходов конкретного блока, поэтому для каждого блока
// создаётся свой экземпляр с теми же TypeId «fn-params» и «fn-return».

/// <summary>Начало схемы своего блока: отдаёт значения, пришедшие на входы блока.</summary>
public sealed class CustomParamsNode(GraphCustomBlock block) : NodeDefinition
{
    public const string Id = "fn-params";

    public override string TypeId => Id;
    public override string Label => "Параметры";
    public override string Category => NodeCategories.Io;
    public override string Description => "Начало схемы своего блока. Отдаёт значения, которые пришли на входы блока снаружи.";
    public override bool IsEntry => true;
    public override int? MinPerGraph => 1;
    public override int? MaxPerGraph => 1;

    public override IReadOnlyList<PortDef> Ports { get; } =
    [
        Port.ExecOut("next", "далее"),
        .. block.Inputs.Select(port => Port.DataOut(port.Id, port.Name, port.Type)),
    ];

    public override ExecResult Execute(NodeContext context)
    {
        foreach (var port in Outputs.Where(port => port.IsData))
            context.SetOutput(port.Id, context.Argument(port.Id));
        return ExecResult.Next("next", "Параметры блока получены");
    }
}

/// <summary>Конец схемы своего блока: значения на входах становятся выходами блока.</summary>
public sealed class CustomReturnNode(GraphCustomBlock block) : NodeDefinition
{
    public const string Id = "fn-return";

    public override string TypeId => Id;
    public override string Label => "Вернуть";
    public override string Category => NodeCategories.Io;
    public override string Description => "Завершает схему своего блока. Значения на входах становятся выходами блока снаружи.";
    public override int? MinPerGraph => 1;

    public override IReadOnlyList<PortDef> Ports { get; } =
    [
        Port.ExecIn(),
        .. block.Outputs.Select(port => Port.DataIn(port.Id, port.Name, port.Type)),
    ];

    public override ExecResult Execute(NodeContext context)
    {
        foreach (var port in Inputs.Where(port => port.IsData))
            context.Return(port.Id, context.Input(port.Id));
        return ExecResult.Finish(Value.Null, "Результат блока");
    }
}
