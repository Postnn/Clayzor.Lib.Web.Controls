using Clayzor.Lib.Web.Controls.Components.Filter;
using Clayzor.Lib.Web.Controls.Components.Grid;

namespace Clayzor.Lib.Web.Controls.Components.Tree;

/// <summary>
/// Описание одной колонки дерева, доступной для фильтрации в <see cref="ClayFilterDialog"/>.
/// Страница явно задаёт список таких колонок в <see cref="ClayTreeOptions.FilterColumns"/>.
/// Аналог <c>ClayColumnDef</c> грида, но без гридовых понятий (Groupable, ColumnId и др.).
/// </summary>
public sealed class ClayTreeFilterColumn
{
    /// <summary>SQL-имя колонки — попадает в условие фильтра.</summary>
    public required string SqlName { get; init; }

    /// <summary>Отображаемое имя колонки (в диалоге, чипах, тексте фильтра).</summary>
    public required string DisplayName { get; init; }

    /// <summary>Тип данных колонки — определяет доступные операторы и редактор значения.</summary>
    public ColumnType ColumnType { get; init; } = ColumnType.Text;

    /// <summary>Справочник значений для выпадающего выбора; null — типозависимый редактор.</summary>
    public IReadOnlyList<ClayFilterOption>? Options { get; init; }

    /// <summary>Подпись значения true булевой колонки; null — «Да».</summary>
    public string? BoolTrueLabel { get; init; }

    /// <summary>Подпись значения false булевой колонки; null — «Нет».</summary>
    public string? BoolFalseLabel { get; init; }
}
