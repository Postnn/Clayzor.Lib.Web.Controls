> Часть серии **TF**. Прочитать `TF0_README_tree_filter.md` и отчёт **TF_A**.
> Делать ТОЛЬКО этот шаг.

# TF_B — настройки: `ClayTreeDynamicSettings` + новые свойства `ClayTreeOptions`

Заводим настройки под фильтр/состояние дерева. Пока **никто их не использует** — только
объявления, регистрация в DI и тесты байнда. Функционал подключается в TF_C…TF_I.

## Конвенция (CGO/CGR1) — что куда

- **`ClayTreeDynamicSettings`** — уровень приложения, из конфигурации, через DI (образец —
  `ClayGridDynamicSettings`): имя таблицы настроек, имя параметра сохранения фильтра/состояния,
  имя CLID-параметра.
- **`ClayTreeOptions`** — настройки экземпляра дерева на странице: `MaxFilterRecords`, список
  исключённых из фильтрации колонок, значения по умолчанию, сопоставление query-параметров,
  `SelectionMode`, флаги показа.

## Прочитать

- отчёт **TF_A** (пункты 5, 7, 8 — источник колонок, persist-инфраструктура, форма состояния);
- `Components/Grid/Dynamic/ClayGridDynamicSettings.cs` — **образец** класса `*Settings`:
  свойства, `Validate()`, тексты исключений с именем класса;
- `Components/Grid/Dynamic/ServiceCollectionExtensions.cs` — как регистрируется
  (`AddClayGridDynamic`, `Configure<>`, `IValidateOptions<>`);
- `Components/Tree/ClayTreeOptions.cs` — текущий состав;
- `Services/ServiceCollectionExtensions.cs` — где `AddClayTree()`;
- `tests/.../OptionsBindingTests.cs`, `ClayTreeOptionsTests.cs` — образцы тестов.

## 1. `ClayTreeDynamicSettings`

Файл `Components/Tree/ClayTreeDynamicSettings.cs`, неймспейс
`Clayzor.Lib.Web.Controls.Components.Tree`. По образцу `ClayGridDynamicSettings`:

```csharp
/// <summary>
/// Настройки уровня приложения для сохранения состояния и фильтра дерева ClayTreeView:
/// имена объектов БД и параметров. Байндятся из конфигурации, живут в DI.
/// Не путать с ClayTreeOptions (настройки экземпляра дерева на странице).
/// </summary>
public sealed class ClayTreeDynamicSettings
{
    /// <summary>Имя таблицы пользовательских параметров (та же, что у грида).</summary>
    public string UserParamsTable { get; set; } = "";

    /// <summary>Префикс имени параметра для сохранения фильтра дерева.</summary>
    public string FilterParamPrefix { get; set; } = "";

    /// <summary>Префикс имени параметра для сохранения состояния дерева (якорь + выделение).</summary>
    public string StateParamPrefix { get; set; } = "";

    /// <summary>Имя query-параметра идентификатора клиента (CLID).</summary>
    public string ClientIdQueryParam { get; set; } = "";

    /// <summary>Проверяет полноту настроек; бросает исключение с русским текстом и именем класса.</summary>
    public void Validate() { /* по образцу ClayGridDynamicSettings; текст начинается с "ClayTreeDynamicSettings." */ }
}
```

Точный состав — **по отчёту TF_A п.7**: если таблица настроек, схема имён и CLID-параметр у
дерева те же, что у грида, часть значений может **переиспользоваться из `ClayGridDynamicSettings`**,
а не дублироваться. Решить по TF_A:
- если таблица/CLID общие с гридом → `ClayTreeDynamicSettings` хранит **только** префиксы,
  специфичные для дерева (`FilterParamPrefix`, `StateParamPrefix`), а таблицу/CLID берёт из
  `ClayGridDynamicSettings` (инжектить оба). Это предпочтительно — один источник имени таблицы;
- если независимы → полный самостоятельный класс.
Выбор обосновать в отчёте; не дублировать имя таблицы в двух местах без нужды.

Секция конфигурации — по образцу `"ClayGrid:Dynamic"`, например `"ClayTree:Dynamic"`.

## 2. Регистрация в DI

В `Services/ServiceCollectionExtensions.cs`, метод `AddClayTree` — принять `IConfiguration`
(сейчас параметра нет — добавить перегрузку или параметр, не ломая существующий вызов) и:

```csharp
services.Configure<ClayTreeDynamicSettings>(config.GetSection("ClayTree:Dynamic"));
services.AddSingleton<IValidateOptions<ClayTreeDynamicSettings>, ValidateClayTreeDynamicSettings>();
```

`ValidateClayTreeDynamicSettings` — `internal sealed`, по образцу гридового валидатора.
Обновить вызов `AddClayTree()` в `Program.cs` `Kesco.App.Web.Inventory` — передать конфигурацию.
Добавить секцию `"ClayTree:Dynamic"` в `appsettings.json` стенда (значения — по TF_A п.7).

## 3. Новые свойства `ClayTreeOptions`

Добавить (блоками-разделителями, докстринг на каждом; **пока не используются**):

```csharp
// ── Фильтрация ──────────────────────────────────────────────
/// <summary>Максимум СОВПАДЕНИЙ, показываемых при фильтре (предки не в счёт). 0 — без лимита? — уточнить.</summary>
public int MaxFilterRecords { get; set; } = 100;

/// <summary>SQL-имена колонок, исключённых из фильтрации (не предлагаются в диалоге).</summary>
public IReadOnlyList<string> FilterExcludedColumns { get; set; } = [];

/// <summary>Значения фильтра по умолчанию: SqlName → значение. Подставляются как WHERE в ленивом режиме.</summary>
public IReadOnlyDictionary<string, object?> FilterDefaults { get; set; } = new Dictionary<string, object?>();

/// <summary>Сопоставление имён query-параметров строки запроса колонкам: имя параметра → SqlName.</summary>
public IReadOnlyDictionary<string, string> FilterQueryParamMap { get; set; } = new Dictionary<string, string>();

// ── Выбор ───────────────────────────────────────────────────
/// <summary>Режим выделения узлов. В текущей версии поддерживается None и Single.</summary>
public ClayTreeSelectionMode SelectionMode { get; set; } = ClayTreeSelectionMode.Single;

// ── Список фильтруемых колонок ──────────────────────────────
// Состав — по решению TF_A п.5 (источник колонок и типов). Возможно:
/// <summary>Явный список фильтруемых колонок дерева (если типы/имена не выводятся из запроса).</summary>
public IReadOnlyList<ClayTreeFilterColumn>? FilterColumns { get; set; }
```

`ClayTreeSelectionMode` — новый enum `{ None, Single, Multiple }`; в докстринге `Multiple` —
«задел, в текущей версии не реализовано». `ClayTreeFilterColumn` (если нужен по TF_A) —
описание колонки дерева для фильтра (SqlName, DisplayName, тип); в TF_C маппится в
`ClayFilterColumnInfo`. Если TF_A показал, что колонки берутся иначе — привести в соответствие.

**`MaxFilterRecords` = 100 по умолчанию** (значение из задания). Уточнить в отчёте семантику 0.

## 4. Тесты

- `ClayTreeOptionsTests` — защёлка на **новые** дефолты (`MaxFilterRecords == 100`,
  `SelectionMode == Single`, коллекции — пустые непустой-ссылкой); старые ожидания не трогать;
- `OptionsBindingTests` (или новый `TreeSettingsBindingTests`) — байнд `ClayTreeDynamicSettings`
  из in-memory `IConfiguration` и `Validate()` (пустое значение → исключение, текст содержит
  `ClayTreeDynamicSettings.`).

## Не делай

- Не подключай настройки к загрузке/фильтру/панели — только объявления и DI (это TF_C…TF_I).
- Не дублируй имя таблицы настроек, если оно общее с гридом (переиспользуй, см. п.1).
- Не клади `MaxFilterRecords` в параметр тега `ClayTreeView` — только в options (правило CGO).
- Не трогай грид, `ClayGridDynamicSettings`, общий фильтр.

## Проверка

- `dotnet build` + `dotnet test` — зелёные, новые тесты видны;
- `grep -rn "ClayTreeDynamicSettings" src/` → класс, валидатор, регистрация, тест — и ничего
  лишнего;
- секция `"ClayTree:Dynamic"` есть в `appsettings.json` стенда, значения доходят до кода;
- `ClayTreeView` не приобрёл новых параметров тега;
- дерево на `/tree-test` работает как до шага (настройки пока пассивны).
