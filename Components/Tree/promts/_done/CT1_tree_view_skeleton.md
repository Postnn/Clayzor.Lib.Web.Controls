# CT1 — `ClayTreeView`: структура классов, ленивая загрузка уровней, тестовая страница

> **Ревизия 2** (после серий **CGO** и **CGR1**). Первая редакция делила настройки на «плоские
> параметры тега для источника данных» + «`ClayTreeOptions` для поведения». После CGO в
> библиотеке зафиксировано другое правило, и эта редакция ему подчинена. Список изменений — §11.
>
> Промт для агента. Стиль исполнения — по корневому `AGENTS.md`:
> **Think Before Coding → Simplicity First → Surgical Changes → Goal-Driven Execution.**
> Весь пользовательский текст — **на русском**. Каждый новый public/protected член — с `/// <summary>`.
> Git (add/commit/push) — **только по прямому указанию**. `AGENTS.md`, `README.md` и `docs/`
> **не обновлять** без отдельной просьбы.
>
> **Это первый шаг серии CT.** Делай РОВНО этот файл и остановись. Дорожная карта CT2+ — в §10,
> она нужна только чтобы структура классов была расширяемой, а НЕ чтобы её реализовать.
> Если реальная сигнатура/поведение существующего кода расходится с этим промтом —
> **ОСТАНОВИСЬ и спроси**, не додумывай.

---

## 1. Задача (требования заказчика)

Новый компонент библиотеки `Clayzor.Lib.Web.Controls` — дерево `ClayTreeView`
(визуальный ориентир — MudBlazor TreeView, https://mudblazor.com/components/treeview).

1. Источник данных — **SQL-таблица**. Поддерживаются **два режима иерархии**, переключаемых
   настройкой:
   - **`NestedSet`** — модель вложенных множеств (левый/правый ключ + уровень);
   - **`ParentKey`** — простая ссылка на родителя (одно поле «Родитель»).
2. Каждый узел имеет **идентификатор** и **текстовое представление**.
3. **Каждый уровень подгружается с сервера** отдельным запросом (lazy load). Дерево целиком
   в память не читается.
4. Дерево **сохраняет своё состояние** — набор раскрытых узлов; при повторном открытии/возврате
   на страницу раскрытые ветки восстанавливаются (с догрузкой их уровней).
5. Тестовая страница — в приложении `Kesco.App.Web.Inventory`.
6. **Главное требование этого шага: структура классов и настроек должна легко расширяться.**
   Дальше на неё будут навешиваться выделение узла (с каскадом на потомков и родителей), поиск,
   drag-and-drop, контекстное меню, чекбоксы. Ничего из этого сейчас НЕ реализуется, но точки
   расширения должны быть предусмотрены.

**Объём CT1: скелет + рабочая ленивая загрузка + восстановление раскрытого состояния +
тестовая страница.** Выделение, поиск, DnD, чекбоксы, контекстное меню — НЕ в этом шаге.

---

## 2. Контекст

- Стек: **.NET 10**, Blazor Server (Interactive Server), **MudBlazor 9.x**.
  `Clayzor.Lib.Web.Controls` — Razor Class Library.
- Целевая СУБД — **SQL Server 2008 R2**. `OFFSET/FETCH` **запрещён**.
- Структура решения:
  ```
  Clayzor.Lib.DALC          — DbManager, ISqlErrorHandler
        ▲
  Clayzor.Lib.Entities      — сущности, весь SQL (в т.ч. DynamicGrid/DynamicSql)
        ▲
  Clayzor.Lib.Web.Controls  — UI-компоненты (ClayGrid, теперь ClayTreeView)
        ▲
  Kesco.App.Web.Inventory   — приложение-стенд
  ```
- **Ключевое архитектурное правило решения:** слой `Controls` **никогда не создаёт `DbManager`
  и никогда не выполняет SQL сам**. Весь SQL живёт в `Clayzor.Lib.Entities` (для произвольных
  запросов — `Clayzor.Lib.Entities.DynamicGrid.DynamicSql`). Controls вызывает классы данных из
  `Lib.Entities`, передавая инжектированный `DbManager Db`.
- **Конвенция настроек компонентов** (зафиксирована серией CGO, записана в
  `src/Clayzor.Lib.Web.Controls/AGENTS.md`, раздел «Настройки компонентов (Options)»):
  - **`Clay*Options`** — конфигурация одного экземпляра компонента на странице;
  - **`Clay*Settings`** — настройки уровня приложения (из `appsettings`/`web.config`, через DI):
    `ClayAppSettings`, `ClayGridDynamicSettings`;
  - параметрами тега остаются **только** данные, меняющиеся между рендерами, `RenderFragment`,
    `EventCallback` и живые ссылки на потребителя;
  - объект настроек создаётся страницей **один раз в поле**, а не выражением в разметке.

  **Это действующее правило библиотеки, а не пожелание.** Прочитай его в `AGENTS.md` до кода;
  если формулировка там расходится с этим промтом — приоритет у `AGENTS.md`, и это повод
  остановиться и спросить.

---

## 3. Прочитать перед началом (обязательно, до написания кода)

**Не выдумывай API — используй методы, которые реально есть.**

| Файл | Зачем |
|---|---|
| `/AGENTS.md` | общие правила исполнения, отчётность, запрет на самовольные коммиты |
| `src/Clayzor.Lib.Web.Controls/AGENTS.md`, раздел **«Настройки компонентов (Options)»** | правило, которому подчинён §4 этого промта |
| `Components/Grid/ClayGridOptions.cs` | **эталон класса настроек**: оформление, блоки-разделители, докстринги, дефолты |
| `Components/Grid/ClayGrid.razor.cs` | как выглядит компонент после CGO: параметр `Options`, состав оставшихся `[Parameter]` |
| `src/Kesco.App.Web.Inventory/Components/Pages/Home.razor` | **эталон разметки страницы** после CGO (поле настроек + два атрибута у тега). Директивы страницы копировать оттуда дословно |
| `src/Clayzor.App.Web.MedicalTests/Components/Pages/MedicalTests.razor` | второй эталон: сборка настроек в `OnInitialized`, когда нужны инжектированные сервисы |
| `docs/component-options-template.md` | **если файл существует** (создаётся опциональным шагом CGO D1) — следовать ему |
| `src/Clayzor.Lib.Entities/AGENTS.md`, раздел **DynamicGrid** | правила слоя данных |
| `src/Clayzor.Lib.Entities/DynamicGrid/DynamicSql.cs` | **точные сигнатуры** `QueryRowsAsync` и др. — их и вызывать |
| `src/Clayzor.Lib.Entities/DynamicGrid/ClayGridSchemaMap.cs` | как оформляется «маппинг имён колонок» — повторить приём для дерева |
| `src/Clayzor.Lib.Entities/DynamicGrid/ClayGridDefinitionData.cs` | образец класса данных: чистые билдеры SQL + async-исполнение |
| `Components/Grid/ClayGrid.Grouping.cs` | образец работы с раскрытым состоянием (`HashSet` ключей) |
| `Components/Grid/Dynamic/GridStateSerializer.cs` | стиль сериализации состояния (чистые функции) |
| `Components/Grid/ClayGrid.Dynamic.cs` | образец «не падать, а показать сообщение» (`_dynamicError`) |
| `Components/ClayCheckbox.razor` | образец маленького компонента и XML-докстрингов |
| `Services/ServiceCollectionExtensions.cs` и `Components/Grid/Dynamic/ServiceCollectionExtensions.cs` | куда добавить `AddClayTree()`; как оформлена регистрация у грида |
| `tests/Clayzor.Lib.Web.Controls.Tests/ClayGridOptionsTests.cs` | образец теста-защёлки на дефолты (обязателен и для дерева) |
| `wwwroot/css/clay.css` | куда пишутся стили компонентов RCL |
| `STYLE_RULES.md` + `build/StyleGuard.targets` | что запрещено в inline-стилях (сборка падает) |
| `src/Kesco.App.Web.Inventory/Components/_Imports.razor` | куда добавить `@using` для дерева |
| `src/Kesco.App.Web.Inventory/Components/Layout/NavMenu.razor` (если есть) | ссылка на тестовую страницу — **только если файл существует** |
| `scripts/dynamic-grid/schema.sql` | образец оформления SQL-скрипта стенда |

---

## 4. Зафиксированные решения (не пересматривать, не «улучшать»)

1. **Вся конфигурация — в `ClayTreeOptions`.** Включая источник данных (`SelectSql`,
   `HierarchyMode`, `Schema`, `OrderBy`, `RootId`) и `TreeId`. Параметрами тега остаются только:
   `Options`, `DataSource` (живая ссылка на подменный источник), `NodeTemplate`
   (`RenderFragment`) и четыре `EventCallback`. Итого **7 параметров** — и так должно остаться
   после CT2–CT8: новая функция добавляет свойство в options.
2. **Никакого `_opt`/`ResolveOptions`.** Composition-с-legacy-параметрами в `ClayGrid`
   существует исключительно ради обратной совместимости на время миграции. У нового компонента
   миграции нет: он читает `Options.X` напрямую. Копировать `_opt` из грида — карго-культ.
3. **`Options` обязателен** (`[EditorRequired]`). У грида он опционален (грид со всеми дефолтами
   осмыслен), у дерева — нет: без `SelectSql` и `TreeId` компонент бессмыслен.
   В `OnInitializedAsync` проверить непустоту `TreeId` и `SelectSql` → `InvalidOperationException`
   с внятным русским текстом. Это новый код, а не рефакторинг, — ранняя валидация уместна.
4. **`ClayTreeOptions` живёт рядом с компонентом** — `Components/Tree/ClayTreeOptions.cs`,
   не в подпапке `Models/`: это публичная настройка компонента, а не внутренняя модель
   (у грида — `Components/Grid/ClayGridOptions.cs`).
5. **Плоский класс настроек**, тематические блоки-разделители внутри, никаких вложенных секций
   (`Options.Data.SelectSql`) — как у `ClayGridOptions`. Порог пересмотра — ~30 свойств.
6. **Статический `Defaults` не добавлять.** У грида он нужен как фолбэк в `ClayGridPageBase`
   (`Grid?.Options ?? ClayGridOptions.Defaults`); у дерева потребителя нет, а публичный член без
   потребителя — обещание, которое никто не поддерживает.
   **Тест-защёлка на дефолты обязательна** (§9) — она `Defaults` не требует.
7. **Идентификатор узла в UI-модели — `string`, а не `int`.** Обоснование: у `ClayGrid` выбор
   строк сделан на `HashSet<int>`, и это уже привело к ограничению «гриды с нечисловым ID
   остаются без выбора» (GF13/GB1). Дерево не повторяет эту ошибку. В SQL значение уходит
   **параметром** как `object?` — типизацию делает Dapper.
8. **Два режима иерархии — один компонент, один контракт загрузки.** Различие живёт ТОЛЬКО
   в билдере SQL (`ClayTreeSqlBuilder`) и в том, какие поля узла заполнены.
9. **В режиме `NestedSet` колонка уровня обязательна.** Без неё «прямые дети» не выражаются
   одним предикатом. `LevelColumn` не задан → `InvalidOperationException` с внятным русским
   текстом при инициализации (ошибка программиста, не пользователя).
10. **В режиме `ParentKey` уровень узла вычисляется в C#** как `Level родителя + 1`
    (корни — 0). Из SQL он не приходит.
11. **Признак «есть дети» приходит из SQL**, а не выясняется отдельным запросом:
    - `NestedSet`: `[Правый] - [Левый] > 1`;
    - `ParentKey`: `EXISTS (SELECT 1 FROM (src) c WHERE c.[Родитель] = s.[Id])`.
12. **Своя разметка узла (`ClayTreeNodeView`), а не `MudTreeView`/`MudTreeViewItem`.**
    Обоснование то же, по которому в `ClayGrid` запрещена встроенная группировка MudBlazor:
    состоянием (раскрытие, догрузка уровня, в будущем — трёхпозиционное выделение и DnD) должен
    владеть наш компонент, а `MudTreeView` в 9.x владеет им сам через
    `TreeItemData<T>`/`ServerData`. Визуально имитируем MudTreeView (шеврон + текст, отступ по
    уровню), иконки — из `Icons.Material`, стили — в `clay.css`.
13. **Состояние (раскрытые узлы) — за абстракцией `IClayTreeStateStore`.** В CT1 реализация
    одна — `ClayTreeMemoryStateStore` (Scoped, живёт в пределах circuit'а: переходы между
    страницами состояние держат, полная перезагрузка браузера — теряет). Персистенция в БД —
    шаг CT5; интерфейс уже такой, чтобы подключить её без правки компонента.
    **Класс настроек для CT5, когда понадобится, называется `ClayTreeSettings`**, а не
    `*Options` (конвенция §2): он будет читать имена таблиц из конфигурации.
14. **Стенд — своя демо-таблица**, создаваемая скриптом (`scripts/tree/schema.sql`).
    Существующие таблицы БД Inventory **не использовать и не угадывать**.
15. **Никакого рекурсивного CTE и никакой загрузки поддерева целиком.** Один клик по шеврону =
    один запрос за один уровень. Требование заказчика, а не деталь реализации.

---

## 5. Ловушки — прочитай до кода

**1. Настройки не собирать в разметке.** `Options="@(new ClayTreeOptions { … })"` создаёт новый
объект на каждый рендер. Для грида это уже запрещено правилом библиотеки и проверяется grep'ом;
для дерева запрет тот же. Поле страницы — `readonly` с инициализатором, если значения
константны, или сборка в `OnInitialized`, если нужны инжектированные сервисы (в инициализаторе
поля DI ещё не отработал → `NullReferenceException`).

**2. `Left` и `Right` — ключевые слова T-SQL.** Любое имя колонки в генерируемом SQL —
**всегда в квадратных скобках**. Псевдонимы выходных колонок — тоже (`AS [_left]`).

**3. Имена колонок в SQL vs значения.** Имена колонок подставляются в текст запроса (приходят
из схемы — доверенный источник, но **провалидируй**: пустые/`null` → исключение). Значения
(`@parentId`, `@left`, `@right`, `@level`) — **только через Dapper-параметры**. `ORDER BY` —
не свободная строка: каждый идентификатор в нём обязан быть именем колонки из схемы или из
`ExtraColumns`, иначе исключение.

**4. StyleGuard роняет сборку** при визуальных inline-стилях в `.razor`/`.cs` (`color`,
`background*`, `border*`, `font-*`, hex, `rgba(`). Структурные inline (`padding-left` для
отступа по уровню, `display`, `width`) — разрешены. Цвета, рамки, типографика узла — только
классами в `clay.css`.

**5. Корневой уровень — это НЕ «родитель = 0».** В `ParentKey` дефолт — `[Родитель] IS NULL`,
но встречаются схемы с `0` или ссылкой на себя. Поэтому в схеме есть `RootParentValue`:
`null` → генерируется `IS NULL`, иначе → `= @rootParent`. Не хардкодь `IS NULL`.

**6. `DbManager` не реентерабельный, MARS выключен.** Не запускай два запроса уровней
параллельно (`Task.WhenAll` по узлам — запрещено). Восстановление состояния — строго
последовательный обход сверху вниз.

**7. Дважды не грузить.** У узла есть `IsLoaded` и `IsLoading`. Повторный клик по шеврону
раскрытого узла не должен инициировать запрос; узел с `IsLoading == true` игнорирует клики.

**8. Сравнение настроек в `OnParametersSet`.** Страница держит **один и тот же** объект
`ClayTreeOptions` между рендерами и может менять его свойства. Поэтому «настройки изменились»
определяется сравнением значимых значений (`SelectSql`, `HierarchyMode`, `RootId`) с
запомненными в полях, а не сравнением ссылки на `Options`. Иначе получишь либо бесконечный
цикл перезагрузок, либо игнорирование изменений.

---

## 6. Что создать — инвентарь файлов

### 6.1 Слой данных — `src/Clayzor.Lib.Entities/Tree/`

| Файл | Содержание |
|---|---|
| `ClayTreeHierarchyMode.cs` | `enum { NestedSet, ParentKey }` |
| `ClayTreeSchema.cs` | имена колонок источника + `ExtraColumns` + `RootParentValue` + `Validate(mode)` |
| `ClayTreeSource.cs` | immutable-описание источника для слоя данных (см. §7.1 про дублирование) |
| `ClayTreeRow.cs` | «сырая» строка уровня: `Id`, `Text`, `ParentId`, `Left`, `Right`, `Level`, `HasChildren`, `Raw` |
| `ClayTreeSqlBuilder.cs` | **чистые** функции построения SQL для обоих режимов (тестируются без БД) |
| `ClayTreeData.cs` | исполнение через `DynamicSql`, маппинг строк в `ClayTreeRow` |

### 6.2 Компонент — `src/Clayzor.Lib.Web.Controls/Components/Tree/`

| Файл | Содержание |
|---|---|
| `ClayTreeOptions.cs` | **настройки компонента** (§7.2) |
| `ClayTreeView.razor` | разметка дерева (контейнер, цикл по корням, индикатор, сообщение об ошибке) |
| `ClayTreeView.razor.cs` | основа: `ComponentBase`, `IClayTreeView`, параметры, поля, инициализация, инжекты |
| `ClayTreeView.Loading.cs` | загрузка корней и уровней, обработка ошибок |
| `ClayTreeView.Expansion.cs` | раскрытие/сворачивание, сохранение/восстановление состояния |
| `ClayTreeNodeView.razor` + `.razor.cs` | рекурсивный рендер одного узла |
| `IClayTreeView.cs` | контракт дерева для страницы и будущих дочерних компонентов (`CascadingValue`) |
| `Models/ClayTreeNode.cs` | UI-модель узла |
| `Models/ClayTreeLoadRequest.cs`, `Models/ClayTreeLoadResult.cs` | контракт загрузки уровня |
| `Models/ClayTreeState.cs` | состояние дерева (пока — только раскрытые узлы) |
| `DataSources/IClayTreeDataSource.cs` | абстракция источника |
| `DataSources/ClaySqlTreeDataSource.cs` | реализация: вызывает `ClayTreeData`, маппит `ClayTreeRow` → `ClayTreeNode` |
| `State/IClayTreeStateStore.cs`, `State/ClayTreeMemoryStateStore.cs` | хранилище состояния |

Плюс: `AddClayTree()` в `Services/ServiceCollectionExtensions.cs`, блок стилей `clay-tree-*`
в `wwwroot/css/clay.css`, `@using` в `_Imports.razor` библиотеки.

### 6.3 Стенд и тесты

| Файл | Содержание |
|---|---|
| `scripts/tree/schema.sql` | демо-таблица + сид (§7.7) |
| `src/Kesco.App.Web.Inventory/Components/Pages/TreeTest.razor` (+ `.razor.cs`) | тестовая страница `/tree-test` |
| `tests/Clayzor.Lib.Web.Controls.Tests/ClayTreeSqlBuilderTests.cs` | тесты билдеров SQL |
| `tests/Clayzor.Lib.Web.Controls.Tests/ClayTreeOptionsTests.cs` | защёлка на значения по умолчанию |

---

## 7. Пошагово

### 7.1 Слой данных: режим, схема, источник

```csharp
namespace Clayzor.Lib.Entities.Tree;

/// <summary>Модель хранения иерархии в источнике данных.</summary>
public enum ClayTreeHierarchyMode
{
    /// <summary>Модель вложенных множеств: левый/правый ключ + уровень.</summary>
    NestedSet = 0,

    /// <summary>Простая ссылка на родителя (adjacency list).</summary>
    ParentKey = 1,
}
```

`ClayTreeSchema`: `IdColumn`, `TextColumn`, `ParentColumn?`, `LeftColumn?`, `RightColumn?`,
`LevelColumn?`, `RootParentValue` (`object?`), `ExtraColumns` (`IReadOnlyList<string>`),
метод `Validate(ClayTreeHierarchyMode mode)` с русскими текстами исключений. Каждое свойство —
с докстрингом; в докстринге каждой колонки указать, для какого режима она обязательна.

```csharp
/// <summary>
/// Описание источника данных дерева для слоя данных: запрос, режим иерархии, схема, сортировка.
/// </summary>
/// <remarks>
/// Дублирует часть свойств ClayTreeOptions намеренно. ClayTreeOptions живёт в
/// Clayzor.Lib.Web.Controls, а зависимость направлена Controls → Entities: слой данных
/// не может его видеть. Поэтому у него свой immutable-тип, а компонент собирает
/// ClayTreeSource из настроек в ОДНОМ месте (ClaySqlTreeDataSource).
/// </remarks>
public sealed record ClayTreeSource(
    string SelectSql,
    ClayTreeHierarchyMode Mode,
    ClayTreeSchema Schema,
    string? OrderBy = null,
    object? RootId = null);
```

Причину дублирования пропиши именно так — иначе следующий читатель начнёт «устранять
дублирование» и получит ссылку из Entities на Controls, то есть циклическую зависимость.

### 7.2 `ClayTreeOptions`

`Components/Tree/ClayTreeOptions.cs`, namespace `Clayzor.Lib.Web.Controls.Components.Tree`.
Оформление — **строго по образцу `ClayGridOptions`**: `sealed`, POCO с `{ get; set; }`, дефолты
в объявлении, тематические блоки-разделители, докстринг на каждом свойстве, абзац в заголовке
класса про «создаётся один раз в поле страницы» и фраза про отличие от `Clay*Settings`.

| Блок | Свойства (дефолт) |
|---|---|
| Идентификация | `TreeId` (`""`, обязателен к заполнению) |
| Источник данных | `SelectSql` (`""`), `HierarchyMode` (`NestedSet`), `Schema` (`new()`), `OrderBy` (`null`), `RootId` (`null`) |
| Загрузка | `LazyLoad` (`true`; в CT1 поддерживается только `true`), `InitialExpandLevel` (`0`) |
| Состояние | `PersistExpandedState` (`true`) |
| Внешний вид | `IndentPx` (`20`), `ShowLoadingIndicator` (`true`), `Class` (`null`), `Style` (`null`) |

`Class`/`Style` — тоже конфигурация, а не данные: по конвенции они в options, а не атрибутами
тега. Отметь это в докстринге, иначе кто-то по привычке добавит их параметрами.

### 7.3 `ClayTreeRow` и `ClayTreeSqlBuilder`

`ClayTreeRow`: `object? Id`, `string Text`, `object? ParentId`, `long? Left`, `long? Right`,
`int? Level`, `bool HasChildren`, `IReadOnlyDictionary<string, object?> Raw`.

Фиксированные псевдонимы выходных колонок (чтобы не конфликтовать с `ExtraColumns`):
`[_id]`, `[_text]`, `[_parent]`, `[_left]`, `[_right]`, `[_level]`, `[_haschildren]`.
Вынеси в `public const string` — на них ссылаются билдер, маппер и тесты.

```csharp
/// <summary>
/// Построение SQL для ленивой загрузки одного уровня дерева.
/// Все методы — чистые функции (тестируются без БД).
/// </summary>
public static class ClayTreeSqlBuilder
{
    /// <summary>Имя параметра идентификатора родителя.</summary>
    public const string ParentParam = "parentId";
    /// <summary>Имена параметров границ и уровня родителя (режим NestedSet).</summary>
    public const string LeftParam = "left", RightParam = "right", LevelParam = "level";
    /// <summary>Имя параметра «значение ссылки на корень» (режим ParentKey).</summary>
    public const string RootParentParam = "rootParent";

    /// <summary>SQL для загрузки одного уровня. isRoot = true — корневой уровень.</summary>
    public static string BuildLevelSql(ClayTreeSource src, bool isRoot);

    /// <summary>SQL для загрузки одного узла по идентификатору.</summary>
    public static string BuildNodeSql(ClayTreeSource src);

    /// <summary>Проверяет, что каждый идентификатор в ORDER BY — известная колонка схемы.</summary>
    public static string BuildOrderBy(ClayTreeSource src);
}
```

**`ParentKey`, уровень под родителем:**

```sql
SELECT s.[КодУзла] AS [_id], s.[Название] AS [_text], s.[КодРодителя] AS [_parent],
       CASE WHEN EXISTS (SELECT 1 FROM (<SelectSql>) c WHERE c.[КодРодителя] = s.[КодУзла])
            THEN 1 ELSE 0 END AS [_haschildren]
FROM (<SelectSql>) s
WHERE s.[КодРодителя] = @parentId
ORDER BY s.[Название]
```

Корневой уровень — тот же запрос, но `WHERE`:
- `RootParentValue is null` → `s.[КодРодителя] IS NULL`;
- иначе → `s.[КодРодителя] = @rootParent`;
- если задан `RootId` → `s.[КодРодителя] = @parentId` со значением `RootId`.

**`NestedSet`, уровень под родителем:**

```sql
SELECT s.[КодУзла] AS [_id], s.[Название] AS [_text], s.[КодРодителя] AS [_parent],
       s.[ЛевыйКлюч] AS [_left], s.[ПравыйКлюч] AS [_right], s.[Уровень] AS [_level],
       CASE WHEN s.[ПравыйКлюч] - s.[ЛевыйКлюч] > 1 THEN 1 ELSE 0 END AS [_haschildren]
FROM (<SelectSql>) s
WHERE s.[ЛевыйКлюч] > @left AND s.[ПравыйКлюч] < @right AND s.[Уровень] = @level + 1
ORDER BY s.[ЛевыйКлюч]
```

Корневой уровень (без `RootId`):
`WHERE s.[Уровень] = (SELECT MIN(m.[Уровень]) FROM (<SelectSql>) m)`.
`_parent` в `NestedSet` включается в SELECT **только если** `ParentColumn` задан.
Сортировка по умолчанию: `NestedSet` → `[ЛевыйКлюч]`; `ParentKey` → `TextColumn`.

### 7.4 `ClayTreeData`

```csharp
/// <summary>
/// Класс данных дерева: выполняет запросы уровней через <see cref="DynamicSql"/>.
/// DbManager не создаёт — получает параметром (правило слоя данных решения).
/// </summary>
public static class ClayTreeData
{
    /// <summary>Загружает один уровень: детей узла или корневой уровень.</summary>
    public static Task<List<ClayTreeRow>> LoadLevelAsync(
        DbManager db, ClayTreeSource src, ClayTreeRow? parent, CancellationToken ct = default);

    /// <summary>Загружает один узел по идентификатору; null — узла нет.</summary>
    public static Task<ClayTreeRow?> LoadNodeAsync(
        DbManager db, ClayTreeSource src, object? id, CancellationToken ct = default);
}
```

Внутри: `BuildLevelSql` → `DynamicParameters` → `DynamicSql.QueryRowsAsync(...)` → маппинг
словаря в `ClayTreeRow` (по константам-псевдонимам; `_text` → `?.ToString() ?? ""`;
`_haschildren` → `Convert.ToInt32(...) == 1`; `Raw` — ключи, не начинающиеся с `_`).

### 7.5 UI-модель и контракты

`ClayTreeNode`: `Id` (`string`, `required`), `RawId` (`object?` — уходит параметром в SQL),
`Text`, `ParentId`, `Level`, `Left`/`Right`, `HasChildren`, `IsExpanded`, `IsLoaded`,
`IsLoading`, `Children`, `Parent`, `Raw` (доп. колонки — задел на будущее). Всё с докстрингами.

`ClayTreeLoadRequest` (`Parent`), `ClayTreeLoadResult` (`Nodes`, `Error`),
`IClayTreeDataSource.LoadLevelAsync(request, ct)`.

`ClaySqlTreeDataSource(DbManager db, ClayTreeSource source)` — собирает `ClayTreeRow` родителя
из `ClayTreeNode`, зовёт `ClayTreeData`, маппит обратно; в `ParentKey` подставляет
`Level = parent?.Level + 1 ?? 0`. Строку ключа получать одним приватным хелпером
`ToKey(object? value)` → `value?.ToString() ?? ""` и использовать везде, чтобы ключи состояния
совпадали.

`ClayTreeState` (`HashSet<string> ExpandedIds`), `IClayTreeStateStore` (`LoadAsync(treeId, ct)`,
`SaveAsync(treeId, state, ct)`), `ClayTreeMemoryStateStore` (Scoped,
`Dictionary<string, ClayTreeState>`; в докстринге честно: состояние живёт в пределах circuit'а,
полная перезагрузка страницы его теряет, персистенция в БД — CT5).

### 7.6 Компонент

`IClayTreeView`: `string TreeId { get; }`, `ClayTreeHierarchyMode HierarchyMode { get; }`,
`IReadOnlyList<ClayTreeNode> RootNodes { get; }`, `IReadOnlySet<string> ExpandedIds { get; }`,
`Task ReloadAsync()`, `Task ExpandAsync(string id)`, `Task CollapseAsync(string id)`.

`ClayTreeView.razor.cs` — **ровно семь параметров**:

| Параметр | Тип | Назначение |
|---|---|---|
| `Options` | `ClayTreeOptions` (`[EditorRequired]`) | вся конфигурация |
| `DataSource` | `IClayTreeDataSource?` | подмена источника (тесты, нестандартные источники) |
| `NodeTemplate` | `RenderFragment<ClayTreeNode>?` | своя разметка узла |
| `OnNodeClick` | `EventCallback<ClayTreeNode>` | клик по тексту узла |
| `OnNodeExpanded` | `EventCallback<ClayTreeNode>` | раскрытие |
| `OnNodeCollapsed` | `EventCallback<ClayTreeNode>` | сворачивание |
| `OnLoadError` | `EventCallback<string>` | ошибка загрузки уровня |

Понадобился восьмой — **стоп**: скорее всего это конфигурация, и её место в `ClayTreeOptions`.

Инжекты: `DbManager Db`, `IClayTreeStateStore StateStore`.

`OnInitializedAsync`: проверить `Options` и непустоту `TreeId`/`SelectSql` (решение 3), вызвать
`Options.Schema.Validate(Options.HierarchyMode)` → собрать `ClayTreeSource` → взять
`DataSource ?? new ClaySqlTreeDataSource(Db, source)` → загрузить корни → восстановить состояние.

`OnParametersSet`: при смене `SelectSql`/`HierarchyMode`/`RootId` — полная перезагрузка;
сравнение — по значениям, запомненным в полях (ловушка 8).

`ClayTreeView.Loading.cs`: `LoadRootsAsync()`, `EnsureChildrenLoadedAsync(node)` (`IsLoading` →
`StateHasChanged()` → загрузка → `Parent`/`IsLoaded` → снять `IsLoading`), поле `_error` +
`OnLoadError` (образец — `_dynamicError` в `ClayGrid.Dynamic.cs`), плоский индекс
`Dictionary<string, ClayTreeNode> _byId` для `ExpandAsync(id)`.

`ClayTreeView.Expansion.cs`: `HashSet<string> _expanded`, `ToggleAsync(node)` (при сворачивании
детей из памяти **не** выбрасывать), `SaveStateAsync()` при `Options.PersistExpandedState`,
`RestoreStateAsync()` — последовательный обход сверху вниз (ловушка 6); отсутствующие в данных
узлы состояния молча игнорировать и вычищать из набора; `InitialExpandLevel` применять только
если сохранённого состояния нет.

`ClayTreeView.razor` — `div.clay-tree` (класс + `Options.Class`/`Options.Style`), сообщение об
ошибке (`MudAlert Severity.Warning`, как `.clay-grid-error`), индикатор первой загрузки, цикл
по корням, `CascadingValue<IClayTreeView>` вокруг содержимого.

`ClayTreeNodeView.razor` — строка узла и рекурсия: шеврон (`ChevronRight`/`ExpandMore`),
`MudProgressCircular Size.Small` при `IsLoading`, распорка той же ширины при отсутствии детей
(иначе текст «прыгает»); отступ `style="padding-left:@(Node.Level * Tree.IndentPx)px"`; текст
через `NodeTemplate` или `<span class="clay-tree-node-text">`; при `IsExpanded` — цикл по
`Children`.

Классы в `clay.css`: `.clay-tree`, `.clay-tree-node`, `.clay-tree-node-row`,
`.clay-tree-node-text`, `.clay-tree-node-toggle`, `.clay-tree-node-spacer`, `.clay-tree-empty`,
`.clay-tree-error`. Ориентир — MudTreeView: строка 32px, hover через
`--mud-palette-action-default-hover`, `cursor:pointer` на тексте. Тёмная тема обязана работать →
только переменные `--mud-palette-*`, никаких своих hex-цветов.

**DI** — в существующий `ServiceCollectionExtensions`:

```csharp
/// <summary>Регистрирует сервисы компонента ClayTreeView.</summary>
public static IServiceCollection AddClayTree(this IServiceCollection services)
{
    services.AddScoped<IClayTreeStateStore, ClayTreeMemoryStateStore>();
    return services;
}
```

Вызов — в `Program.cs` приложения `Kesco.App.Web.Inventory`, рядом с `AddClayGridDynamic()`.
Параметр `IConfiguration` этому методу **не нужен**: настроек уровня приложения у дерева пока
нет (появятся в CT5 — и тогда это `ClayTreeSettings`, решение 13).

### 7.7 `scripts/tree/schema.sql`

Идемпотентный скрипт (`IF OBJECT_ID(...) IS NULL`), таблица `dbo.ClayTreeDemo` — **обе модели
в одной таблице**, чтобы одни данные показать двумя способами:

```sql
CREATE TABLE dbo.ClayTreeDemo (
    КодУзла      int           NOT NULL PRIMARY KEY,
    Название     nvarchar(200) NOT NULL,
    КодРодителя  int           NULL,
    ЛевыйКлюч    int           NOT NULL,
    ПравыйКлюч   int           NOT NULL,
    Уровень      int           NOT NULL
);
```

Сид — 14 узлов, 2 корня, 3 уровня:

| Код | Название | Родитель | L | R | Уровень |
|---|---|---|---|---|---|
| 1 | Оборудование | NULL | 1 | 24 | 0 |
| 2 | Компьютерная техника | 1 | 2 | 13 | 1 |
| 3 | Ноутбуки | 2 | 3 | 8 | 2 |
| 4 | Ноутбук Dell Latitude | 3 | 4 | 5 | 3 |
| 5 | Ноутбук HP ProBook | 3 | 6 | 7 | 3 |
| 6 | Мониторы | 2 | 9 | 12 | 2 |
| 7 | Монитор Dell 24" | 6 | 10 | 11 | 3 |
| 8 | Мебель | 1 | 14 | 21 | 1 |
| 9 | Столы | 8 | 15 | 18 | 2 |
| 10 | Стол письменный | 9 | 16 | 17 | 3 |
| 11 | Стулья | 8 | 19 | 20 | 2 |
| 12 | Прочее | 1 | 22 | 23 | 1 |
| 13 | Программное обеспечение | NULL | 25 | 28 | 0 |
| 14 | Лицензии | 13 | 26 | 27 | 1 |

Проверь арифметику вложенных множеств сам (`R = L + 2*(число потомков) + 1`) и добавь в конец
скрипта контрольный `SELECT`, который её валидирует. Расходится — исправляй данные, а не код.

### 7.8 Тестовая страница `/tree-test`

`src/Kesco.App.Web.Inventory/Components/Pages/TreeTest.razor` (+ `.razor.cs`). Директивы —
**дословно как в `Home.razor`**. Разметка — по эталону `Home.razor` после CGO:

```razor
<ClayTreeView Options="_treeOptions" OnNodeClick="OnNodeClick" />
```

Настройки — поля, а не выражение в разметке (ловушка 1). Режимов два, поэтому и объектов
настроек два, с **разными `TreeId`** — иначе состояния перемешаются:

```razor
@code {
    private readonly ClayTreeOptions _nestedOptions = new() { TreeId = "tree-test-nested", … };
    private readonly ClayTreeOptions _parentOptions = new() { TreeId = "tree-test-parent",  … };

    private ClayTreeOptions _treeOptions => _nested ? _nestedOptions : _parentOptions;
}
```

Ещё на странице: переключатель режима (`MudToggleGroup` или две кнопки), панель с последним
нажатым узлом (Id, Text, Level, HasChildren) и строкой «Раскрыто узлов: N», кнопка «Обновить» →
`ReloadAsync()`.

Ссылку в `NavMenu.razor` добавить, **только если файл существует**; текст — «Тест дерева».

---

## 8. Не делай

- **Не оставляй конфигурацию параметрами тега.** `SelectSql`, `HierarchyMode`, `Schema`,
  `TreeId`, `OrderBy`, `RootId`, `Class`, `Style` — в `ClayTreeOptions`. Семь параметров,
  не восемь.
- **Не собирай `ClayTreeOptions` в разметке** (`Options="@(new ClayTreeOptions{…})"`).
- **Не копируй из грида `_opt`/`ResolveOptions`/`CollectLegacyConflicts`/`DiffFromDefaults`** —
  это механика обратной совместимости на время миграции, у нового компонента её нет (решение 2).
- **Не выполняй SQL в `Clayzor.Lib.Web.Controls`** и не создавай там `DbManager`.
- **Не устраняй «дублирование»** между `ClayTreeOptions` и `ClayTreeSource` — см. §7.1.
- **Не используй `MudTreeView` / `MudTreeViewItem` / `TreeItemData<T>` / `ServerData`.**
- **Не грузи дерево целиком**, не пиши рекурсивный CTE, не грузи «два уровня вперёд».
- **Не используй `OFFSET/FETCH`**; виртуализации/пагинации в CT1 нет.
- **Не запускай запросы уровней параллельно** (`Task.WhenAll`).
- **Не делай `Id` целочисленным** и не заводи `HashSet<int>` под состояние.
- **Не реализуй** выделение, каскад на потомков/родителей, чекбоксы, поиск, DnD, контекстное
  меню, редактирование, пересчёт ключей вложенных множеств — это CT2+.
- **Не трогай `Components/Grid/`**, `ClayGridOptions`, `ClayGridDynamicSettings`; не рефактори
  общее (`ClayCheckbox`, `ClayButton`), не переименовывай существующее.
- **Не используй существующие таблицы БД Inventory** и не угадывай их имена.
- **Не пиши визуальные inline-стили** в `.razor`/`.cs` — StyleGuard уронит сборку.
- **Не обновляй** `AGENTS.md`, `README.md`, `docs/`, `STYLE_RULES.md` — что стоит туда дописать,
  перечисли в отчёте.
- **Не делай git-коммит** без прямого указания.

---

## 9. Проверка

**Сборка и тесты**
- `dotnet build Clayzor.sln` — зелёный, без новых warning'ов;
- `dotnet test tests\Clayzor.Lib.Web.Controls.Tests` — зелёный.

**Тесты, которые обязаны появиться**
- `ClayTreeSqlBuilderTests.cs`:
  - `NestedSet` под родителем: предикаты по `[Левый] >`, `[Правый] <`, `[Уровень] = @level + 1`;
    `_haschildren` через разность ключей;
  - `NestedSet` корневой уровень: подзапрос `MIN([Уровень])`;
  - `NestedSet` без `LevelColumn` → `InvalidOperationException` при `Validate`;
  - `ParentKey` корневой уровень: `RootParentValue = null` → `IS NULL`; `= 0` → `@rootParent`;
  - `ParentKey`: `_haschildren` через `EXISTS`;
  - `ExtraColumns` попадают в SELECT-лист, значения — нет;
  - `OrderBy` с колонкой не из схемы → исключение;
  - все идентификаторы в сгенерированном SQL — в квадратных скобках.
- `ClayTreeOptionsTests.cs` — защёлка на дефолты (образец: `ClayGridOptionsTests`):
  `HierarchyMode == NestedSet`, `LazyLoad == true`, `PersistExpandedState == true`,
  `ShowLoadingIndicator == true`, `IndentPx == 20`, `InitialExpandLevel == 0`,
  `TreeId`/`SelectSql` — пустые строки, `Schema` — не `null`. В комментарии к классу — зачем
  тест нужен (контракт для страниц, не задающих настройку явно), иначе следующий читатель
  удалит «бессмысленный» тест.

**Механическая**
- `grep -rn "DbManager" src/Clayzor.Lib.Web.Controls/Components/Tree/` → только инжект
  компонента и передача в конструктор `ClaySqlTreeDataSource`; ни одного `new DbManager`;
- `grep -rn "\[Parameter\]" src/Clayzor.Lib.Web.Controls/Components/Tree/ClayTreeView*.cs` →
  **ровно 7 попаданий**;
- `grep -rn "Options=\"@(new ClayTreeOptions" src/` → пусто.

**Стенд** (`scripts/tree/schema.sql` выполнен, страница `/tree-test`)
- два корня: «Оборудование» и «Программное обеспечение», у обоих шеврон; у «Прочее»
  и «Лицензии» шеврона нет;
- клик по шеврону «Оборудование» → появились «Компьютерная техника», «Мебель», «Прочее»
  с отступом на один уровень;
- в профайлере SQL: на это раскрытие — **ровно один** запрос, и в нём предикат уровня;
- раскрыть до третьего уровня → видны ноутбуки, шеврона у них нет;
- свернуть и снова раскрыть → **нового запроса нет**;
- переключить режим на «Простой родитель» → состав и порядок те же (внутри уровня — по
  названию), в запросе `EXISTS`, а не разность ключей;
- клик по тексту узла → в панели его Id/Text/Level;
- раскрыть три ветки → уйти на `/` → вернуться на `/tree-test` → те же три ветки раскрыты,
  счётчик совпадает;
- «Обновить» → дерево перегрузилось, раскрытое состояние сохранилось;
- негативный: `SelectSql` на несуществующую таблицу → сообщение об ошибке на месте дерева,
  приложение не падает. Вернуть;
- негативный: `NestedSet` без `LevelColumn` → внятное русское исключение при инициализации,
  а не `NullReferenceException`;
- негативный: `TreeId = ""` → внятное русское исключение (решение 3);
- тёмная тема → дерево читаемо, hover виден;
- `/medical-tests` и `/?id=140` работают как до задачи.

**Отчёт:** что создано; какие решения промта пришлось уточнить и почему; расхождения с
конвенцией из `AGENTS.md`, если нашлись; найденные проблемы чужого кода (не исправлять —
перечислить); что стоит дописать в `AGENTS.md` и `docs/`.

---

## 10. Дорожная карта (НЕ делать в CT1)

| Шаг | Тема | Куда ляжет |
|---|---|---|
| CT2 | Выделение узла (одиночное), `SelectedId` в состоянии | `ClayTreeView.Selection.cs`, `ClayTreeState`; флаги — в `ClayTreeOptions` |
| CT3 | Каскадное выделение потомков и родителей, tri-state чекбоксы | `ClayTreeView.Selection.cs`; потомки в `NestedSet` — один запрос по диапазону `[Левый]..[Правый]`, в `ParentKey` — обход загруженного поддерева |
| CT4 | Поиск узла с автораскрытием пути | `ClayTreeSqlBuilder.BuildPathSql` (в `NestedSet` предки одним запросом `L < x AND R > y`) |
| CT5 | Персистенция состояния в БД | вторая реализация `IClayTreeStateStore` по образцу `ClayGridUserParamsData`; её конфигурация — **`ClayTreeSettings`** (уровень приложения, из `appsettings`), а не `*Options`; компонент не меняется |
| CT6 | Контекстное меню и действия над узлом | дочерние компоненты через `CascadingValue<IClayTreeView>` |
| CT7 | Drag-and-drop, перемещение ветки | пересчёт ключей вложенных множеств — только в `Lib.Entities`, транзакцией |
| CT8 | Виртуализация больших уровней | `ClayTreeNodeView` + постраничная догрузка уровня |

Захотелось «сразу заложить» что-то из таблицы кодом — не надо. Достаточно, чтобы для этого
не пришлось ломать созданные контракты.

---

## 11. Что изменилось относительно первой редакции

Причина всех изменений одна: серия **CGO** свела параметры `ClayGrid` в `ClayGridOptions` и
зафиксировала конвенцию в `AGENTS.md`, а **CGR1** переименовал `ClayGridDynamicOptions` →
`ClayGridDynamicSettings`.

| Было | Стало |
|---|---|
| `SelectSql`, `HierarchyMode`, `Schema`, `OrderBy`, `RootId`, `TreeId`, `Class`, `Style` — плоские параметры тега; `Options` — только поведение | **вся конфигурация в `ClayTreeOptions`**; параметров тега 7 |
| `Options` необязателен, дефолт — новый объект | `Options` обязателен (`[EditorRequired]`) + валидация `TreeId`/`SelectSql` |
| `ClayTreeOptions` в `Components/Tree/Models/` | `Components/Tree/ClayTreeOptions.cs` — рядом с компонентом, как `ClayGridOptions` |
| про тесты — только `ClayTreeSqlBuilderTests` | добавлен обязательный `ClayTreeOptionsTests` (защёлка на дефолты — правило библиотеки) |
| эталоны разметки не назывались | эталоны — `Home.razor` и `MedicalTests.razor` **после CGO**; `ClayGridOptions.cs` — эталон класса настроек |
| про `Clay*Settings` ничего не было | конвенция `*Options` / `*Settings` в §2 и решении 13; CT5 получает `ClayTreeSettings` |
| ловушек 7 | добавлена 8-я: сравнение настроек в `OnParametersSet` по значениям, а не по ссылке на `Options` |
| — | новые запреты в §8: не собирать options в разметке, не копировать `_opt`/`ResolveOptions`, не «устранять дублирование» с `ClayTreeSource` |
