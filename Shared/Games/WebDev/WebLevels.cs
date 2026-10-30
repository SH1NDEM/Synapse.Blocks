using Synapse.Blocks.Graph;

namespace Synapse.Blocks.Games.WebDev;

/// <summary>Блок «Форма»: что житель ввёл в поля анкеты.</summary>
public sealed class FormNode : NodeDefinition
{
    public const string Id = "web-form";

    public override string TypeId => Id;
    public override string Label => "Форма";
    public override string Category => NodeCategories.Game;
    public override string Description => "Анкета с сайта: имя, возраст и электронная почта, которые ввёл житель.";

    public override IReadOnlyList<PortDef> Ports { get; } =
    [
        Port.DataOut("name", "имя", DataType.Text),
        Port.DataOut("age", "возраст", DataType.Number),
        Port.DataOut("email", "почта", DataType.Text),
    ];

    public override Value Evaluate(NodeContext context, string outputPort)
    {
        var items = context.ProgramInput.Items;
        if (items.Length < 3)
            throw context.Error("Блок «Форма» работает только в игре «Веб-разработчик».");
        return outputPort switch
        {
            "name" => items[0],
            "age" => items[1],
            _ => items[2]
        };
    }
}

public static class WebNodes
{
    /// <summary>Встроенные блоки и блок «Форма».</summary>
    public static NodeRegistry Registry { get; } = Create();

    private static NodeRegistry Create()
    {
        var registry = NodeRegistry.Default.CloneDefinitions();
        registry.Register(new FormNode());
        return registry;
    }
}

/// <summary>Задания веб-разработчика. Пока одно: проверка формы регистрации.</summary>
public static class WebLevels
{
    public static IReadOnlyList<WebLevel> All { get; } =
    [
        new WebLevel
        {
            Id = Guid.Parse("7e0de700-0000-4000-8000-000000000001"),
            Order = 1,
            Title = "Форма регистрации",
            Site = "library.synapse.city",
            Objective = "Принимайте анкету, только если возраст от 7 лет И в почте есть «@». Иначе — показывайте ошибку.",
            Description = "Городская библиотека запускает сайт. Чтобы записаться, житель заполняет **форму**: имя, возраст и почту. Программа сайта должна **проверить поля**, прежде чем сохранить анкету.",
            Hint = "Две проверки: «Сравнение» возраст ≥ 7 и «Поиск в тексте» — почта содержит «@». Соедините их блоком «И / ИЛИ» в режиме «И» и подайте в «Выход».",
            Forms =
            [
                new(1, "Аня", 10, "anya@mail.ru", true, "всё заполнено верно."),
                new(2, "Петя", 5, "petya@mail.ru", false, "Пете 5 лет, а записаться можно с 7."),
                new(3, "Оля", 12, "olya.mail.ru", false, "в почте нет «@» — письмо не дойдёт."),
                new(4, "Миша", 7, "misha@synapse.city", true, "Мише ровно 7 — уже можно."),
                new(5, "Кирилл", 9, "", false, "поле почты пустое."),
                new(6, "Даша", 15, "dasha@ya.ru", true, "всё заполнено верно."),
            ],
            AllowedNodes = [FormNode.Id, "const", "compare", "text-check", "logic", "if-compare", "if"],
        },
    ];
}
