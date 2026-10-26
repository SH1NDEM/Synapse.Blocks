using Synapse.Blocks.Graph;

namespace Synapse.Blocks.Games.Security;

/// <summary>Блок «Запрос»: что пришло к серверу и от кого.</summary>
public sealed class RequestNode : NodeDefinition
{
    public const string Id = "request";

    public override string TypeId => Id;
    public override string Label => "Запрос";
    public override string Category => NodeCategories.Game;
    public override string Description => "Запрос к серверу города: номер, адрес отправителя, порт (дверь сервера), действие, пароль и сколько раз отправитель уже ошибся с паролем.";

    public override IReadOnlyList<PortDef> Ports { get; } =
    [
        Port.DataOut("number", "номер", DataType.Number),
        Port.DataOut("from", "откуда", DataType.Text),
        Port.DataOut("port", "порт", DataType.Number),
        Port.DataOut("action", "действие", DataType.Text),
        Port.DataOut("password", "пароль", DataType.Text),
        Port.DataOut("attempts", "ошибок", DataType.Number),
    ];

    public override Value Evaluate(NodeContext context, string outputPort)
    {
        var items = context.ProgramInput.Items;
        if (items.Length < 6)
            throw context.Error("Блок «Запрос» работает только в игре «Защита серверов».");
        return outputPort switch
        {
            "number" => items[0],
            "from" => items[1],
            "port" => items[2],
            "action" => items[3],
            "password" => items[4],
            _ => items[5]
        };
    }
}

public static class SecurityNodes
{
    /// <summary>Встроенные блоки и блок «Запрос».</summary>
    public static NodeRegistry Registry { get; } = Create();

    private static NodeRegistry Create()
    {
        var registry = NodeRegistry.Default.CloneDefinitions();
        registry.Register(new RequestNode());
        return registry;
    }
}

/// <summary>Задания защиты: открытые порты, чёрный список, пароли, подбор пароля и защита в несколько слоёв.</summary>
public static class SecurityLevels
{
    private const string Site = "открыть сайт";
    private const string Login = "вход";
    private const string SignUp = "регистрация";

    private static readonly string[] Blacklist = ["66.6.6.6", "13.13.0.13"];

    public static IReadOnlyList<SecurityLevel> All { get; } =
    [
        new SecurityLevel
        {
            Id = Guid.Parse("5a7e0003-0000-4000-8000-000000000001"),
            Order = 1,
            Title = "Открытые порты",
            Objective = "Пропускайте только запросы на порт 443 — это сайт города. Остальные порты закрыты.",
            Description = "У сервера много «дверей» — **портов**. Жителям нужна только одна: порт **443**, через который открывается сайт. Остальные двери взломщики пытаются открыть, чтобы пробраться внутрь.",
            Hint = "«Сравнение»: a — «порт», знак «=», b — 443. Его ответ («да» или «нет») подайте прямо в «Выход».",
            Requests =
            [
                new(1, "77.12.5.9", 443, Site, false, "житель открывает сайт города."),
                new(2, "203.0.113.7", 22, "вход администратора", true, "чужой пытается войти в панель управления сервером через порт 22."),
                new(3, "81.4.4.20", 443, Site, false, "житель смотрит расписание автобусов."),
                new(4, "198.51.100.3", 23, "старый вход telnet", true, "взломщик стучится в старую незащищённую дверь — порт 23."),
                new(5, "77.12.5.9", 443, Site, false, "житель снова открывает сайт."),
                new(6, "203.0.113.7", 3389, "удалённый рабочий стол", true, "взломщик пытается управлять сервером удалённо через порт 3389."),
            ],
            AllowedNodes = [RequestNode.Id, "const", "compare", "if-compare", "if"],
        },
        new SecurityLevel
        {
            Id = Guid.Parse("5a7e0003-0000-4000-8000-000000000002"),
            Order = 2,
            Title = "Чёрный список",
            Objective = "Блокируйте запросы с адресов из чёрного списка: 66.6.6.6 и 13.13.0.13. Остальных пропускайте.",
            Description = "Служба безопасности знает адреса, с которых уже нападали на город. Их собрали в **чёрный список**. Все запросы идут на сайт, но от «чёрных» адресов их нужно отбивать.",
            Hint = "Константа со списком: [\"66.6.6.6\", \"13.13.0.13\"]. «Есть в списке»: список — константа, значение — «откуда». Ответ переверните блоком «НЕ» и подайте в «Выход».",
            Blacklist = Blacklist,
            Requests =
            [
                new(1, "77.12.5.9", 443, Site, false, "житель открывает сайт города."),
                new(2, "66.6.6.6", 443, Site, true, "этот адрес в чёрном списке — с него уже нападали."),
                new(3, "81.4.4.20", 443, Site, false, "житель читает новости."),
                new(4, "13.13.0.13", 443, Site, true, "адрес из чёрного списка."),
                new(5, "91.200.1.8", 443, Site, false, "новый житель — его адреса нет в списке."),
                new(6, "66.6.6.6", 443, Site, true, "тот же взломщик пробует снова."),
            ],
            AllowedNodes = [RequestNode.Id, "const", "list-has", "not", "compare", "if-compare", "if"],
        },
        new SecurityLevel
        {
            Id = Guid.Parse("5a7e0003-0000-4000-8000-000000000003"),
            Order = 3,
            Title = "Надёжные пароли",
            Objective = "При регистрации пропускайте только пароли длиной от 8 символов.",
            Description = "Жители заводят аккаунты на сайте города. Короткий пароль взломщик подберёт за секунды, поэтому сервер должен **не принимать слабые пароли**: в пароле должно быть не меньше **8 символов**.",
            Hint = "«Длина» пароля → «Сравнение» с 8 (знак «≥») → «Выход».",
            Requests =
            [
                new(1, "77.12.5.9", 443, SignUp, false, "пароль «solnce2026» достаточно длинный.", Password: "solnce2026"),
                new(2, "81.4.4.20", 443, SignUp, true, "пароль «123» подберут за секунду.", Password: "123"),
                new(3, "91.200.1.8", 443, SignUp, false, "пароль «Kot_Barsik_7» надёжный.", Password: "Kot_Barsik_7"),
                new(4, "45.7.7.21", 443, SignUp, true, "пароль «qwerty» — один из самых частых в мире.", Password: "qwerty"),
                new(5, "62.3.3.14", 443, SignUp, false, "пароль «Luna-Park8» ровно 10 символов.", Password: "Luna-Park8"),
                new(6, "62.3.3.15", 443, SignUp, false, "пароль «Синапс!1» ровно 8 символов — граница, его можно принять.", Password: "Синапс!1"),
                new(7, "45.7.7.22", 443, SignUp, true, "пароль «1111» слишком короткий.", Password: "1111"),
            ],
            AllowedNodes = [RequestNode.Id, "const", "length", "compare", "if-compare", "if"],
        },
        new SecurityLevel
        {
            Id = Guid.Parse("5a7e0003-0000-4000-8000-000000000004"),
            Order = 4,
            Title = "Подбор пароля",
            Objective = "Пропускайте вход, пока отправитель ошибся с паролем меньше 3 раз. После 3 ошибок — блокируйте.",
            Description = "Взломщик не знает пароль и перебирает варианты один за другим. Обычный человек ошибается раз-другой, а программа-взломщик — десятки раз подряд. Выход «ошибок» показывает, сколько раз этот адрес уже **ввёл неправильный пароль**.",
            Hint = "«Сравнение»: «ошибок» < 3 → «Выход».",
            Requests =
            [
                new(1, "77.12.5.9", 443, Login, false, "житель входит с первой попытки.", Attempts: 0),
                new(2, "203.0.113.7", 443, Login, false, "первая попытка — пока не отличить от обычного человека.", Attempts: 0),
                new(3, "203.0.113.7", 443, Login, false, "одна ошибка — с кем не бывает.", Attempts: 1),
                new(4, "81.4.4.20", 443, Login, false, "житель один раз опечатался.", Attempts: 1),
                new(5, "203.0.113.7", 443, Login, false, "две ошибки — ещё можно попробовать.", Attempts: 2),
                new(6, "203.0.113.7", 443, Login, true, "уже 3 ошибки подряд — это подбор пароля.", Attempts: 3),
                new(7, "203.0.113.7", 443, Login, true, "4 ошибки — взломщик не сдаётся.", Attempts: 4),
                new(8, "81.4.4.20", 443, Login, false, "житель вспомнил пароль.", Attempts: 1),
            ],
            AllowedNodes = [RequestNode.Id, "const", "compare", "if-compare", "if"],
        },
        new SecurityLevel
        {
            Id = Guid.Parse("5a7e0003-0000-4000-8000-000000000005"),
            Order = 5,
            Title = "Защита в несколько слоёв",
            Objective = "Пропускайте запрос, только если порт 443, адреса нет в чёрном списке И ошибок меньше 3.",
            Description = "Атаки стали хитрее: каждая проверка по отдельности где-то пропускает взломщика. Соберите все проверки вместе — **порт**, **чёрный список** и **подбор пароля**. Запрос проходит, только если пройдены все три.",
            Hint = "Три проверки дают три «да/нет». Соедините их блоками «И / ИЛИ» в режиме «И». Удобно собрать каждую проверку в свой блок.",
            Blacklist = Blacklist,
            Requests =
            [
                new(1, "77.12.5.9", 443, Login, false, "житель входит на сайт.", Attempts: 0),
                new(2, "66.6.6.6", 443, Login, true, "адрес из чёрного списка, хоть и стучится в правильную дверь.", Attempts: 0),
                new(3, "91.200.1.8", 22, "вход администратора", true, "незнакомый адрес лезет в панель управления через порт 22.", Attempts: 0),
                new(4, "81.4.4.20", 443, Login, false, "житель один раз ошибся с паролем.", Attempts: 1),
                new(5, "45.7.7.21", 443, Login, true, "адрес не в чёрном списке, но уже 5 раз ошибся — подбор пароля.", Attempts: 5),
                new(6, "13.13.0.13", 3389, "удалённый рабочий стол", true, "адрес из чёрного списка и закрытый порт.", Attempts: 0),
                new(7, "91.200.1.8", 443, Site, false, "новый житель открывает сайт.", Attempts: 0),
            ],
            AllowedNodes = [RequestNode.Id, "const", "list-has", "length", "category:logic"],
        },
    ];
}
