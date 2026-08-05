# CTM1 — Контракт сервиса мутаций `IClayTreeMutations` и enum `ClayTreePathDirection`

## Цель

Ввести интерфейс, через который компонент выполняет ВСЕ изменения данных дерева, и enum
направления построения пути. Реализацию интерфейса пишет приложение; в промте приведён эталонный
SQL каждой операции — приложение может использовать его как есть.

Компонент сам НЕ выполняет модифицирующих запросов и НЕ считает `L`/`R`. Он только вызывает методы
этого сервиса.

## Шаги

### Шаг 1 — файл `Clayzor.Lib.Web.Controls/Components/Tree/ClayTreePathDirection.cs`

Создать enum:

```csharp
namespace Clayzor.Lib.Web.Controls.Components.Tree;

/// <summary>
/// Направление построения полного пути к узлу скалярной SQL-функцией.
/// Значение передаётся вторым параметром функции (@PathType bit).
/// </summary>
public enum ClayTreePathDirection
{
    /// <summary>От потомка к родителю (@PathType = 0).</summary>
    ChildToParent = 0,

    /// <summary>От родителя к потомку (@PathType = 1). Значение по умолчанию.</summary>
    ParentToChild = 1,
}
```

### Шаг 2 — файл `Clayzor.Lib.Web.Controls/Components/Tree/IClayTreeMutations.cs`

Создать интерфейс. Все `object`-параметры — это сырой идентификатор узла (`ClayTreeNode.RawId`) и
сырой идентификатор родителя (`ClayTreeNode.ParentId`), как они приходят из БД. Реализация сама
приводит их к нужному типу параметра.

```csharp
namespace Clayzor.Lib.Web.Controls.Components.Tree;

/// <summary>
/// Сервис изменения данных дерева. Реализуется приложением.
/// Компонент <see cref="ClayTreeView"/> вызывает эти методы; сам модифицирующих
/// запросов не выполняет и L/R не рассчитывает (это делают триггеры БД).
/// <para>
/// Целевой объект всех запросов — тот же, что в <see cref="ClayTreeOptions.SelectSql"/>
/// (таблица или представление с триггерами INSTEAD OF).
/// </para>
/// </summary>
public interface IClayTreeMutations
{
    /// <summary>
    /// Переупорядочивание узла в пределах ОДНОГО родителя (только режим NestedSet).
    /// Компонент передаёт <paramref name="newLeftValue"/> — значение L сиблинга, ПОСЛЕ которого
    /// встаёт узел (для позиции 0 — L текущего первого сиблинга). Триггер нормализует порядок.
    /// </summary>
    /// <param name="nodeId">RawId перемещаемого узла (@Id).</param>
    /// <param name="parentId">ParentId узла — для контроля уровня (@Parent). В UPDATE не участвует.</param>
    /// <param name="newLeftValue">Новое значение L (@NewL).</param>
    /// <remarks>
    /// Эталонный SQL:
    /// <code>UPDATE &lt;SelectSql-объект&gt; SET &lt;LeftColumn&gt; = @NewL WHERE &lt;IdColumn&gt; = @Id;</code>
    /// </remarks>
    Task ReorderAsync(object nodeId, object? parentId, long newLeftValue, CancellationToken ct = default);

    /// <summary>
    /// Переподчинение узла новому родителю (режимы NestedSet и ParentKey — одинаково).
    /// </summary>
    /// <param name="nodeId">RawId перемещаемого узла (@Id).</param>
    /// <param name="newParentId">RawId нового родителя (@Parent). null — сделать корнем.</param>
    /// <remarks>
    /// Эталонный SQL:
    /// <code>UPDATE &lt;SelectSql-объект&gt; SET &lt;ParentColumn&gt; = @Parent WHERE &lt;IdColumn&gt; = @Id;</code>
    /// </remarks>
    Task ReparentAsync(object nodeId, object? newParentId, CancellationToken ct = default);

    /// <summary>
    /// Добавление дочернего узла. L/R проставит триггер. Id новой строки НЕ возвращается
    /// (триггеры INSTEAD OF его не отдают) — компонент затем перезагружает уровень родителя.
    /// </summary>
    /// <param name="parentId">RawId родителя (null — добавить в корень).</param>
    /// <param name="editColumn">SQL-имя редактируемого поля (значение названия).</param>
    /// <param name="value">Значение названия для нового узла.</param>
    Task AddChildAsync(object? parentId, string editColumn, string value, CancellationToken ct = default);

    /// <summary>
    /// Обновление одного поля узла по первичному ключу (= IdColumn).
    /// </summary>
    /// <remarks>
    /// Эталонный SQL:
    /// <code>UPDATE &lt;SelectSql-объект&gt; SET [editColumn] = @Value WHERE &lt;IdColumn&gt; = @Id;</code>
    /// Имя колонки <paramref name="editColumn"/> подставляется в текст запроса — реализация обязана
    /// либо экранировать его как идентификатор ([...]), либо сверять с белым списком.
    /// </remarks>
    Task UpdateNodeAsync(object nodeId, string editColumn, string value, CancellationToken ct = default);

    /// <summary>Удаление узла по первичному ключу (= IdColumn).</summary>
    /// <remarks><code>DELETE FROM &lt;SelectSql-объект&gt; WHERE &lt;IdColumn&gt; = @Id;</code></remarks>
    Task DeleteAsync(object nodeId, CancellationToken ct = default);

    /// <summary>
    /// Полный путь к узлу скалярной функцией. Вызов:
    /// <c>SELECT &lt;functionName&gt;(@Code, @PathType)</c>.
    /// </summary>
    /// <param name="nodeId">RawId узла (@Code).</param>
    /// <param name="functionName">Имя SQL-функции из опций дерева.</param>
    /// <param name="direction">Направление (@PathType): 0 или 1.</param>
    Task<string> GetNodePathAsync(object nodeId, string functionName, ClayTreePathDirection direction, CancellationToken ct = default);

    /// <summary>
    /// Является ли <paramref name="candidateDescendantId"/> потомком <paramref name="ancestorId"/>.
    /// Используется для запрета дропа «родитель → в своего потомка» в режиме ParentKey.
    /// В NestedSet компонент определяет это в памяти по L/R и этот метод не вызывает.
    /// </summary>
    Task<bool> IsDescendantAsync(object candidateDescendantId, object ancestorId, CancellationToken ct = default);
}
```

## Критерии приёмки

- Проект собирается; интерфейс и enum видимы из namespace `...Components.Tree`.
- Никакой реализации в библиотеке компонента не добавлено (реализацию поставляет приложение).
- Все методы принимают `CancellationToken` с дефолтом.

## Примечание для приложения (не часть кода компонента)

Эталонная реализация метода `ReorderAsync` (для справки разработчику приложения; поведение
идентично согласованному примеру заказчика, но с параметром готового `@NewL`):

```sql
UPDATE vwРасположения SET L = @NewL WHERE КодРасположения = @Id;
```

`ReparentAsync`:

```sql
UPDATE vwРасположения SET Parent = @Parent WHERE КодРасположения = @Id;
```
