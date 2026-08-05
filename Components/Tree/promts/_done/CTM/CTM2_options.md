# CTM2 — Новые опции `ClayTreeOptions` для изменения данных

## Цель

Добавить в `ClayTreeOptions` флаги включения операций, параметры диалога/функции пути и список
кастомных операций контекстного меню.

## Шаг 1 — файл `Clayzor.Lib.Web.Controls/Components/Tree/ClayTreeOptions.cs`

Добавить в класс новый регион с опциями. Вставить ПЕРЕД закрывающей скобкой класса.

```csharp
    // ── Изменение данных (CTM) ───────────────────────────────────────────────────

    /// <summary>
    /// Включает drag-and-drop: переупорядочивание (только NestedSet) и переподчинение.
    /// Требует зарегистрированного <see cref="IClayTreeMutations"/>. По умолчанию выключено.
    /// </summary>
    public bool EnableDragDrop { get; set; }

    /// <summary>Включает пункт «Редактировать» в контекстном меню узла. По умолчанию выключено.</summary>
    public bool EnableEdit { get; set; }

    /// <summary>Включает пункт «Добавить дочерний» в контекстном меню узла. По умолчанию выключено.</summary>
    public bool EnableAddChild { get; set; }

    /// <summary>Включает пункт «Удалить» в контекстном меню узла. По умолчанию выключено.</summary>
    public bool EnableDelete { get; set; }

    /// <summary>
    /// SQL-имя редактируемого поля — значение названия узла для диалогов «Редактировать»
    /// и «Добавить дочерний». Обязателен при <see cref="EnableEdit"/> или <see cref="EnableAddChild"/>.
    /// Пример: <c>"НазваниеРасположения"</c>.
    /// </summary>
    public string? EditColumn { get; set; }

    /// <summary>
    /// Имя скалярной SQL-функции полного пути к узлу. Функция принимает два параметра:
    /// <c>@Code int</c> (идентификатор) и <c>@PathType bit</c> (направление). Показывается
    /// в диалоге редактирования/добавления. Если не задано — блок пути в диалоге скрыт.
    /// Пример: <c>"dbo.fnПутьРасположения"</c>.
    /// </summary>
    public string? NodePathFunction { get; set; }

    /// <summary>
    /// Направление построения пути (второй параметр функции). По умолчанию
    /// <see cref="ClayTreePathDirection.ParentToChild"/> (=1).
    /// </summary>
    public ClayTreePathDirection NodePathDirection { get; set; } = ClayTreePathDirection.ParentToChild;

    /// <summary>
    /// Кастомные пункты контекстного меню узла. Добавляются к стандартным
    /// (редактировать/добавить/удалить). Каждый пункт вызывает свой делегат.
    /// </summary>
    public IReadOnlyList<ClayTreeMenuItem> CustomMenuItems { get; set; } = [];
```

## Шаг 2 — файл `Clayzor.Lib.Web.Controls/Components/Tree/ClayTreeMenuItem.cs`

Создать модель пункта кастомного меню:

```csharp
using Clayzor.Lib.Web.Controls.Components.Tree.Models;

namespace Clayzor.Lib.Web.Controls.Components.Tree;

/// <summary>
/// Пользовательский пункт контекстного меню узла дерева.
/// </summary>
public sealed class ClayTreeMenuItem
{
    /// <summary>Подпись пункта меню. Обязательна.</summary>
    public required string Label { get; init; }

    /// <summary>Иконка (значение из <c>MudBlazor.Icons</c>). null — без иконки.</summary>
    public string? Icon { get; init; }

    /// <summary>
    /// Действие при выборе пункта. Получает узел, на котором открыто меню.
    /// Обновление данных/дерева при необходимости выполняет само приложение
    /// (например, вызовом <c>ReloadAsync</c> через ссылку на дерево).
    /// </summary>
    public required Func<ClayTreeNode, Task> OnExecute { get; init; }
}
```

## Критерии приёмки

- Проект собирается.
- Значения по умолчанию: все `Enable*` = `false`, `NodePathDirection` = `ParentToChild`,
  `CustomMenuItems` = пустой список.
- XML-doc на каждом новом члене.
