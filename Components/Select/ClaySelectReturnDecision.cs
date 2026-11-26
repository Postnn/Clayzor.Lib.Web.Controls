namespace Clayzor.Lib.Web.Controls.Components.Select;

/// <summary>Состояние фактического применения выбора вызывающей формой.</summary>
public enum ClaySelectReturnDecision
{
    /// <summary>Значение применено и может быть подтверждено дочернему окну.</summary>
    Accepted,
    /// <summary>Выбор отклонён до изменений; пользователь может повторить отправку.</summary>
    Rejected,
    /// <summary>Вызвавший редактор или контекст запроса уже сменился.</summary>
    Stale,
    /// <summary>Применение нельзя подтвердить; автоматический повтор недопустим.</summary>
    Indeterminate
}
