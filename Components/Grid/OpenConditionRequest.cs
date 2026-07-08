namespace Clayzor.Lib.Web.Controls.Components.Grid;

/// <summary>
/// Сигнал из <see cref="ClayColumnValueFilterDialog"/> открыть диалог фильтра
/// по условию (<see cref="ClayColumnFilterDialog"/>) с указанным начальным оператором.
/// Используется при клике на оператор из контекстного списка условий (треб. 7).
/// </summary>
/// <param name="Operator">Оператор, с которым должен открыться диалог.</param>
public sealed record OpenConditionRequest(ColumnFilterOperator Operator);
