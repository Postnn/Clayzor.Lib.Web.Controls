namespace Clayzor.Lib.Web.Controls.Components.Form;

/// <summary>Настройки <see cref="ClayNumber"/>.</summary>
public sealed class ClayNumberOptions : ClayInputOptions
{
    /// <summary>Минимально допустимое значение.</summary>
    public decimal? Min { get; set; }

    /// <summary>Максимально допустимое значение.</summary>
    public decimal? Max { get; set; }

    /// <summary>Количество знаков после запятой от 0 до 10.</summary>
    public int Precision { get; set; }

    /// <summary>Шаг изменения кнопками.</summary>
    public decimal Step { get; set; } = 1;

    /// <summary>Скрывает кнопки увеличения и уменьшения.</summary>
    public bool HideSpinButtons { get; set; }
}
