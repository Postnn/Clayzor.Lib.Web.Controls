namespace Clayzor.Lib.Web.Controls.Components.Grid.Filter;

/// <summary>
/// Статический хелпер: читаемые русские метки операторов фильтрации.
/// Переиспользуется в <see cref="ClayColumnFilterDialog"/> и диалоге составного фильтра.
/// </summary>
public static class ClayFilterOperatorLabels
{
    /// <summary>Возвращает читаемую русскую метку оператора фильтрации.</summary>
    public static string Get(ColumnFilterOperator op) => op switch
    {
        ColumnFilterOperator.Contains           => "содержит",
        ColumnFilterOperator.NotContains        => "не содержит",
        ColumnFilterOperator.Equals             => "равно",
        ColumnFilterOperator.NotEquals          => "не равно",
        ColumnFilterOperator.StartsWith         => "начинается с",
        ColumnFilterOperator.NotStartsWith      => "не начинается с",
        ColumnFilterOperator.EndsWith           => "заканчивается на",
        ColumnFilterOperator.NotEndsWith        => "не заканчивается на",
        ColumnFilterOperator.GreaterThan        => "больше (>)",
        ColumnFilterOperator.GreaterThanOrEqual => "больше или равно (≥)",
        ColumnFilterOperator.LessThan           => "меньше (<)",
        ColumnFilterOperator.LessThanOrEqual    => "меньше или равно (≤)",
        ColumnFilterOperator.IsEmpty            => "пустая строка",
        ColumnFilterOperator.IsNotEmpty         => "не пустая строка",
        ColumnFilterOperator.IsNull             => "пусто (NULL)",
        ColumnFilterOperator.IsNotNull          => "не пусто (NOT NULL)",
        _                                       => op.ToString(),
    };
}
