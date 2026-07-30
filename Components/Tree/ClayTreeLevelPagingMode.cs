namespace Clayzor.Lib.Web.Controls.Components.Tree;

/// <summary>Способ подгрузки следующей порции детей уровня.</summary>
public enum ClayTreeLevelPagingMode
{
    /// <summary>Кнопка «Загрузить ещё» в конце уровня.</summary>
    Button = 0,
    /// <summary>Автоподгрузка при доскролле до конца загруженной порции.</summary>
    Scroll = 1,
}
