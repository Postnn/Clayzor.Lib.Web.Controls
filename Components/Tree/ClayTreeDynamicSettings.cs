namespace Clayzor.Lib.Web.Controls.Components.Tree;

/// <summary>
/// Настройки уровня приложения для сохранения состояния и фильтра дерева <see cref="ClayTreeView"/>:
/// имена объектов БД и параметров. Байндятся из конфигурации (секция "ClayTree:Dynamic"), живут в DI.
/// Не путать с <see cref="ClayTreeOptions"/> — настройками конкретного экземпляра дерева на странице.
/// </summary>
/// <remarks>
/// Имя таблицы пользовательских параметров и CLID-параметр переиспользуются
/// из <see cref="Grid.Dynamic.ClayGridDynamicSettings"/> — они общие с гридом.
/// Здесь — только префиксы, специфичные для дерева.
/// </remarks>
public sealed class ClayTreeDynamicSettings
{
    /// <summary>Префикс имени параметра для сохранения фильтра дерева.</summary>
    public string FilterParamPrefix { get; set; } = "TreeFilter_";

    /// <summary>Префикс имени параметра для сохранения состояния дерева (якорь + выделение).</summary>
    public string StateParamPrefix { get; set; } = "TreeState_";

    /// <summary>
    /// Проверяет заполненность обязательных полей. Бросает <see cref="InvalidOperationException"/>
    /// с русским текстом и именем класса.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(FilterParamPrefix))
            throw new InvalidOperationException("ClayTreeDynamicSettings.FilterParamPrefix пусто");
        if (string.IsNullOrWhiteSpace(StateParamPrefix))
            throw new InvalidOperationException("ClayTreeDynamicSettings.StateParamPrefix пусто");
    }
}
