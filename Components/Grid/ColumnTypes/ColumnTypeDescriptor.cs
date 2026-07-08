namespace Clayzor.Lib.Web.Controls.Components.Grid.ColumnTypes;

/// <summary>
/// Единая точка типозависимого поведения колонки: операторы, парсинг, формат, SQL-параметр.
/// Один класс-наследник на каждый <see cref="ColumnType"/>.
/// </summary>
public abstract class ColumnTypeDescriptor
{
    /// <summary>Вид колонки (Text/Number/Decimal/Date/Boolean).</summary>
    public abstract ColumnType Kind { get; }

    /// <summary>CLR-тип значения, с которым работает редактор.</summary>
    public abstract Type ClrType { get; }

    /// <summary>Доступные операторы фильтрации.</summary>
    public abstract IReadOnlyList<ColumnFilterOperator> Operators { get; }

    /// <summary>Оператор по умолчанию для нового условия.</summary>
    public virtual ColumnFilterOperator DefaultOperator => Operators[0];

    /// <summary>Требует ли оператор ввода значения (true) или скрывает редактор (false).</summary>
    public virtual bool OperatorTakesValue(ColumnFilterOperator op) => op is not (
        ColumnFilterOperator.IsEmpty or ColumnFilterOperator.IsNotEmpty
        or ColumnFilterOperator.IsNull or ColumnFilterOperator.IsNotNull);

    /// <summary>Парсинг строки в типизированное значение (инвариантная культура).</summary>
    public abstract object? Parse(string? raw);

    /// <summary>Форматирование значения для отображения (чип, описание).</summary>
    public abstract string Format(object? value);

    /// <summary>Преобразование значения перед передачей в Dapper-параметр.</summary>
    public virtual object? ToParameter(object? value) => value;
}
