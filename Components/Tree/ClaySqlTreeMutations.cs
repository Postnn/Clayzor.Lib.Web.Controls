using System.Data;
using Clayzor.Lib.DALC;
using Clayzor.Lib.Entities.Tree;
using Dapper;

namespace Clayzor.Lib.Web.Controls.Components.Tree;

/// <summary>
/// Универсальная реализация <see cref="IClayTreeMutations"/> — выполняет реальные SQL-операции
/// над целевым объектом дерева. Целевой объект и схема колонок задаются при создании.
/// </summary>
public sealed class ClaySqlTreeMutations : IClayTreeMutations
{
    private readonly DbManager _db;
    private readonly string _target;
    private readonly string _idCol;
    private readonly string? _parentCol;
    private readonly string? _leftCol;
    private readonly string? _rightCol;

    /// <summary>
    /// Создаёт экземпляр для конкретного дерева.
    /// </summary>
    /// <param name="db">Менеджер БД с нужной строкой подключения.</param>
    /// <param name="targetObject">Целевой объект SQL (таблица или представление), например "[Ресурсы]".</param>
    /// <param name="schema">Схема колонок из <see cref="ClayTreeOptions.Schema"/>.</param>
    public ClaySqlTreeMutations(DbManager db, string targetObject, ClayTreeSchema schema)
    {
        _db = db;
        _target = targetObject;
        _idCol = Brace(schema.IdColumn);
        _parentCol = schema.ParentColumn is not null ? Brace(schema.ParentColumn) : null;
        _leftCol = schema.LeftColumn is not null ? Brace(schema.LeftColumn) : null;
        _rightCol = schema.RightColumn is not null ? Brace(schema.RightColumn) : null;
    }

    /// <inheritdoc/>
    public async Task ReorderAsync(object nodeId, object? parentId, long newLeftValue, CancellationToken ct = default)
    {
        var sql = $"UPDATE {_target} SET {_leftCol} = @NewL WHERE {_idCol} = @Id";
        await _db.ExecuteAsync(sql, new { Id = nodeId, NewL = newLeftValue }, commandType: CommandType.Text);
    }

    /// <inheritdoc/>
    public async Task ReparentAsync(object nodeId, object? newParentId, CancellationToken ct = default)
    {
        RequireParentColumn();
        var sql = $"UPDATE {_target} SET {_parentCol} = @Parent WHERE {_idCol} = @Id";
        await _db.ExecuteAsync(sql, new { Id = nodeId, Parent = (object?)newParentId ?? DBNull.Value }, commandType: CommandType.Text);
    }

    /// <inheritdoc/>
    public async Task AddChildAsync(object? parentId, string editColumn, string value, CancellationToken ct = default)
    {
        RequireParentColumn();
        ValidateColumn(editColumn);
        var col = Brace(editColumn);
        var sql = $"INSERT INTO {_target} ({_parentCol}, {col}) VALUES (@Parent, @Value)";
        await _db.ExecuteAsync(sql, new { Parent = (object?)parentId ?? DBNull.Value, Value = value }, commandType: CommandType.Text);
    }

    /// <inheritdoc/>
    public async Task UpdateNodeAsync(object nodeId, string editColumn, string value, CancellationToken ct = default)
    {
        ValidateColumn(editColumn);
        var col = Brace(editColumn);
        var sql = $"UPDATE {_target} SET {col} = @Value WHERE {_idCol} = @Id";
        await _db.ExecuteAsync(sql, new { Id = nodeId, Value = value }, commandType: CommandType.Text);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(object nodeId, CancellationToken ct = default)
    {
        var sql = $"DELETE FROM {_target} WHERE {_idCol} = @Id";
        await _db.ExecuteAsync(sql, new { Id = nodeId }, commandType: CommandType.Text);
    }

    /// <inheritdoc/>
    public async Task<string> GetNodePathAsync(object nodeId, string functionName, ClayTreePathDirection direction, CancellationToken ct = default)
    {
        ValidateFunctionName(functionName);
        var sql = $"SELECT {functionName}(@Code, @PathType)";
        var pathType = direction == ClayTreePathDirection.ParentToChild ? 1 : 0;
        var result = await _db.ExecuteScalarAsync<string>(sql, new { Code = nodeId, PathType = pathType }, commandType: CommandType.Text);
        return result ?? "";
    }

    /// <inheritdoc/>
    public async Task<bool> IsDescendantAsync(object candidateDescendantId, object ancestorId, CancellationToken ct = default)
    {
        RequireParentColumn();
        // Рекурсивный CTE: идём от кандидата вверх по Parent, ищем предка.
        var sql = $@"
WITH cte AS (
    SELECT {_idCol} AS Id, {_parentCol} AS Parent
    FROM {_target}
    WHERE {_idCol} = @Candidate
    UNION ALL
    SELECT t.{_idCol}, t.{_parentCol}
    FROM {_target} t
    JOIN cte ON t.{_idCol} = cte.Parent
)
SELECT COUNT(*)
FROM cte
WHERE Id = @Ancestor
OPTION (MAXRECURSION 200)";

        var count = await _db.ExecuteScalarAsync<int>(sql, new { Candidate = candidateDescendantId, Ancestor = ancestorId }, commandType: CommandType.Text);
        return count > 0;
    }

    // ── Хелперы ──────────────────────────────────────────────────────────────────

    /// <summary>Оборачивает имя колонки в квадратные скобки.</summary>
    private static string Brace(string name) => $"[{name}]";

    /// <summary>
    /// Проверяет, что имя колонки состоит только из допустимых символов
    /// (буквы, цифры, подчёркивание). При нарушении — <see cref="ArgumentException"/>.
    /// </summary>
    private static void ValidateColumn(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Имя колонки не может быть пустым.", nameof(name));

        foreach (var c in name)
        {
            if (!char.IsLetterOrDigit(c) && c != '_')
                throw new ArgumentException(
                    $"Недопустимый символ '{c}' в имени колонки '{name}'. " +
                    "Разрешены только буквы, цифры и подчёркивание.", nameof(name));
        }
    }

    /// <summary>
    /// Проверяет, что имя SQL-функции (в т.ч. схема-квалифицированное, например
    /// <c>dbo.fnПуть</c>) состоит только из допустимых символов
    /// (буквы, цифры, подчёркивание, точка-разделитель схемы).
    /// </summary>
    private static void ValidateFunctionName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Имя функции не может быть пустым.", nameof(name));

        foreach (var c in name)
        {
            if (!char.IsLetterOrDigit(c) && c != '_' && c != '.')
                throw new ArgumentException(
                    $"Недопустимый символ '{c}' в имени функции '{name}'. " +
                    "Разрешены только буквы, цифры, подчёркивание и точка.", nameof(name));
        }
    }

    /// <summary>Требует заданной колонки родителя. Используется операциями, работающими с иерархией.</summary>
    private void RequireParentColumn()
    {
        if (_parentCol is null)
            throw new InvalidOperationException(
                "ClayTreeSchema.ParentColumn не задан — операция требует колонку родителя.");
    }
}
