namespace Clayzor.Lib.Web.Controls.Components.Tree;

/// <summary>
/// Сервис изменения данных дерева. Реализуется приложением.
/// Компонент <see cref="ClayTreeView"/> вызывает эти методы; сам модифицирующих
/// запросов не выполняет и L/R не рассчитывает (это делают триггеры БД).
/// <para>
/// Целевой объект всех запросов — <see cref="ClayTreeOptions.TableName"/>
/// (таблица или представление). Если <see cref="ClayTreeOptions.TableName"/> не задан —
/// используется реализация <see cref="IClayTreeMutations"/> из DI.
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
    /// <code>UPDATE &lt;TableName&gt; SET &lt;LeftColumn&gt; = @NewL WHERE &lt;IdColumn&gt; = @Id;</code>
    /// </remarks>
    Task ReorderAsync(object nodeId, object? parentId, long newLeftValue, CancellationToken ct = default);

    /// <summary>
    /// Переподчинение узла новому родителю (режимы NestedSet и ParentKey — одинаково).
    /// </summary>
    /// <param name="nodeId">RawId перемещаемого узла (@Id).</param>
    /// <param name="newParentId">RawId нового родителя (@Parent). null — сделать корнем.</param>
    /// <remarks>
    /// Эталонный SQL:
    /// <code>UPDATE &lt;TableName&gt; SET &lt;ParentColumn&gt; = @Parent WHERE &lt;IdColumn&gt; = @Id;</code>
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
    /// <code>UPDATE &lt;TableName&gt; SET [editColumn] = @Value WHERE &lt;IdColumn&gt; = @Id;</code>
    /// Имя колонки <paramref name="editColumn"/> подставляется в текст запроса — реализация обязана
    /// либо экранировать его как идентификатор ([...]), либо сверять с белым списком.
    /// </remarks>
    Task UpdateNodeAsync(object nodeId, string editColumn, string value, CancellationToken ct = default);

    /// <summary>Удаление узла по первичному ключу (= IdColumn).</summary>
    /// <remarks><code>DELETE FROM &lt;TableName&gt; WHERE &lt;IdColumn&gt; = @Id;</code></remarks>
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
