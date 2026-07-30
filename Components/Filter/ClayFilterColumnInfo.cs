using Clayzor.Lib.Web.Controls.Components.Grid.ColumnTypes;

namespace Clayzor.Lib.Web.Controls.Components.Filter;

/// <summary>
/// Описание одного поля, доступного для фильтрации. НЕ фильтр, а вход для диалогов фильтра:
/// из списка таких описаний диалог знает, какие колонки предлагать, какого они типа и есть ли
/// у них справочник значений. Не зависит от грида — пригоден и для дерева.
/// </summary>
public sealed class ClayFilterColumnInfo
{
    /// <summary>SQL-имя колонки — попадает в условие фильтра.</summary>
    public required string SqlName { get; init; }

    /// <summary>Отображаемое имя колонки (в диалоге, чипах, тексте фильтра).</summary>
    public required string DisplayName { get; init; }

    /// <summary>Дескриптор типа — определяет операторы и редактор значения.</summary>
    public required ColumnTypeDescriptor Type { get; init; }

    /// <summary>Справочник значений для выпадающего выбора; null — типозависимый редактор.</summary>
    public IReadOnlyList<ClayFilterOption>? Options { get; init; }

    /// <summary>Подпись значения true булевой колонки; null — «Да».</summary>
    public string? BoolTrueLabel { get; init; }

    /// <summary>Подпись значения false булевой колонки; null — «Нет».</summary>
    public string? BoolFalseLabel { get; init; }
}
