namespace Clayzor.Lib.Web.Controls.Components.Form;

/// <summary>Явное решение пользователя о несохранённом вводе редактора.</summary>
public enum ClayEditorCancelChoice
{
    /// <summary>Оставить редактор и его ввод.</summary>
    Continue,
    /// <summary>Отказаться от ввода и закрыть редактор.</summary>
    CloseWithoutSaving,
    /// <summary>Восстановить свежие данные и оставить редактор открытым.</summary>
    ResetAndStay
}
