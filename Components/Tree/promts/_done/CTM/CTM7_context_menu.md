# CTM7 — Контекстное меню узла: редактировать, добавить дочерний, удалить, кастомные операции

## Цель

Добавить контекстное меню узла с опциональными пунктами и диалог редактирования/добавления.
Меню показывается, только если включён хотя бы один из `EnableEdit`/`EnableAddChild`/
`EnableDelete` или задан непустой `CustomMenuItems`.

Диалог один и тот же для «Редактировать» и «Добавить дочерний»: поле названия, блок пути,
три кнопки «ОК», «Обновить», «Отмена».

Выполняется ПОСЛЕ CTM6 (использует `ReloadLevelAsync`, `RefreshNodeTextAsync`, `FindNodeById`).

## Шаг 1 — файл `Clayzor.Lib.Web.Controls/Components/Tree/ClayTreeNodeEditDialog.razor` (новый)

Диалог на базе MudBlazor (как прочие диалоги проекта — свериться со стилем существующих, например
диалога фильтра). Параметры:

```razor
@using MudBlazor

<MudDialog>
    <DialogContent>
        @if (!string.IsNullOrEmpty(Path))
        {
            <div class="clay-tree-edit-path">
                <MudText Typo="Typo.caption">Путь:</MudText>
                <MudText Typo="Typo.body2">@Path</MudText>
            </div>
        }
        <MudTextField T="string" @bind-Value="_value" Label="@FieldLabel"
                      Immediate="true" Variant="Variant.Outlined" Margin="Margin.Dense" />
    </DialogContent>
    <DialogActions>
        <MudButton OnClick="Ok" Color="Color.Primary" Variant="Variant.Filled">ОК</MudButton>
        <MudButton OnClick="Refresh">Обновить</MudButton>
        <MudButton OnClick="Cancel">Отмена</MudButton>
    </DialogActions>
</MudDialog>

@code {
    [CascadingParameter] private IMudDialogInstance Dialog { get; set; } = default!;

    /// <summary>Заголовок поля (например «Название»).</summary>
    [Parameter] public string FieldLabel { get; set; } = "Название";

    /// <summary>Начальное значение поля (для редактирования; при добавлении — пусто).</summary>
    [Parameter] public string? InitialValue { get; set; }

    /// <summary>Полный путь к ноде (редактирование) или к родителю (добавление). null — блок скрыт.</summary>
    [Parameter] public string? Path { get; set; }

    /// <summary>
    /// Колбэк «Обновить»: перечитать путь/значение из БД. Возвращает (value, path).
    /// Для добавления value обычно остаётся как есть, обновляется только path родителя.
    /// </summary>
    [Parameter] public Func<Task<(string? Value, string? Path)>>? OnRefresh { get; set; }

    private string _value = "";

    protected override void OnInitialized() => _value = InitialValue ?? "";

    private void Ok() => Dialog.Close(DialogResult.Ok(_value));

    private void Cancel() => Dialog.Cancel();

    private async Task Refresh()
    {
        if (OnRefresh is null) return;
        var (value, path) = await OnRefresh();
        if (value is not null) _value = value;
        Path = path;
        StateHasChanged();
    }
}
```

Тип `IMudDialogInstance` и API диалогов — привести к версии MudBlazor в проекте (в некоторых
версиях `MudDialogInstance`). Свериться с существующими диалогами компонента.

## Шаг 2 — файл `Clayzor.Lib.Web.Controls/Components/Tree/ClayTreeView.Mutations.cs` (дополнить)

Добавить операции меню. Нужен доступ к `IDialogService` — добавить inject в `ClayTreeView.razor.cs`,
если его ещё нет:

```csharp
[Inject] private IDialogService DialogService { get; set; } = default!;
```

Методы операций:

```csharp
public partial class ClayTreeView
{
    // ── Редактирование ───────────────────────────────────────────────────────────

    private async Task EditNodeAsync(ClayTreeNode node)
    {
        if (string.IsNullOrEmpty(Options.EditColumn))
            throw new InvalidOperationException("ClayTreeOptions.EditColumn не задан — редактирование невозможно.");

        var path = await BuildPathAsync(node.RawId);   // путь к самой ноде

        var parameters = new DialogParameters
        {
            ["FieldLabel"]   = "Название",
            ["InitialValue"] = node.Text,
            ["Path"]         = path,
            ["OnRefresh"]    = (Func<Task<(string?, string?)>>)(async () =>
            {
                var freshText = await ReadNodeTextAsync(node);
                var freshPath = await BuildPathAsync(node.RawId);
                return (freshText, freshPath);
            }),
        };

        var dlg = await DialogService.ShowAsync<ClayTreeNodeEditDialog>("Редактирование", parameters);
        var result = await dlg.Result;
        if (result is null || result.Canceled) return;

        var newValue = (string?)result.Data ?? "";
        await Mutations.UpdateNodeAsync(node.RawId!, Options.EditColumn, newValue);

        // Только текст ноды переформировать из БД (уровень не трогаем).
        await RefreshNodeTextAsync(node);
    }

    // ── Добавление дочернего ─────────────────────────────────────────────────────

    private async Task AddChildAsync(ClayTreeNode parent)
    {
        if (string.IsNullOrEmpty(Options.EditColumn))
            throw new InvalidOperationException("ClayTreeOptions.EditColumn не задан — добавление невозможно.");

        var path = await BuildPathAsync(parent.RawId);   // путь к РОДИТЕЛЮ

        var parameters = new DialogParameters
        {
            ["FieldLabel"]   = "Название",
            ["InitialValue"] = "",
            ["Path"]         = path,
            ["OnRefresh"]    = (Func<Task<(string?, string?)>>)(async () =>
            {
                var freshPath = await BuildPathAsync(parent.RawId);
                return (null, freshPath);   // значение не меняем, обновляем только путь родителя
            }),
        };

        var dlg = await DialogService.ShowAsync<ClayTreeNodeEditDialog>("Добавление узла", parameters);
        var result = await dlg.Result;
        if (result is null || result.Canceled) return;

        var value = (string?)result.Data ?? "";
        await Mutations.AddChildAsync(parent.RawId, Options.EditColumn!, value);

        // Триггер INSTEAD OF не вернул Id — перезагружаем уровень родителя.
        // Родитель теперь точно имеет детей.
        parent.HasChildren = true;
        if (!parent.IsExpanded)
        {
            parent.IsExpanded = true;
            _expanded.Add(parent.Id);
        }
        await ReloadLevelAsync(parent);
    }

    // ── Удаление ─────────────────────────────────────────────────────────────────

    private async Task DeleteNodeAsync(ClayTreeNode node)
    {
        var confirmed = await ConfirmAsync($"Вы уверены, что хотите удалить {node.Text}?");
        if (!confirmed) return;

        var parent = node.Parent;   // запомнить до удаления
        await Mutations.DeleteAsync(node.RawId!);

        // Снять выделение, если удаляли выделенный.
        _selectedIds.Remove(node.Id);

        await ReloadLevelAsync(parent);
    }

    // ── Кастомная операция ────────────────────────────────────────────────────────

    private async Task ExecuteCustomAsync(ClayTreeMenuItem item, ClayTreeNode node)
        => await item.OnExecute(node);

    // ── Путь и подтверждение ───────────────────────────────────────────────────────

    /// <summary>Строит путь через сервис мутаций и настроенную функцию. null — функция не задана.</summary>
    private async Task<string?> BuildPathAsync(object? rawId)
    {
        if (rawId is null || string.IsNullOrEmpty(Options.NodePathFunction))
            return null;
        return await Mutations.GetNodePathAsync(rawId, Options.NodePathFunction!, Options.NodePathDirection);
    }

    /// <summary>Диалог подтверждения (да/нет). Возвращает true, если пользователь подтвердил.</summary>
    private async Task<bool> ConfirmAsync(string message)
    {
        // Использовать общий механизм подтверждения проекта, если он есть
        // (ClayConfirm / MudMessageBox). Ниже — вариант через MudBlazor MessageBox.
        var ok = await DialogService.ShowMessageBox(
            "Подтверждение",
            message,
            yesText: "Да", cancelText: "Отмена");
        return ok == true;
    }
}
```

Замечания:
- `DialogParameters`, `ShowAsync`, `DialogResult`, `ShowMessageBox` — привести к API MudBlazor в
  проекте. Если в проекте есть свой сервис подтверждений (`ClayConfirm` или аналог) —
  использовать его вместо `ShowMessageBox`.
- `node.RawId!` не должен быть null для реальных узлов; при добавлении в корень `parent.RawId`
  может быть null — `AddChildAsync(null, ...)` это допускает.

## Шаг 3 — файл `Clayzor.Lib.Web.Controls/Components/Tree/ClayTreeNodeView.razor` (дополнить)

Добавить кнопку-активатор меню в строку узла и само меню. Разместить после блока текста/пометок,
в конце `.clay-tree-node-row`. Меню показывать только если есть хотя бы один пункт.

```razor
@{
    var opt = (Tree as ClayTreeView)?.Options;
    var hasStdMenu = opt is not null &&
        (opt.EnableEdit || opt.EnableAddChild || opt.EnableDelete);
    var hasCustom  = opt?.CustomMenuItems.Count > 0;
    var showMenu   = hasStdMenu || hasCustom;
}

@if (showMenu && Tree is ClayTreeView treeView)
{
    <MudMenu Icon="@Icons.Material.Filled.MoreVert" Size="Size.Small"
             Class="clay-tree-node-menu" Dense="true"
             AnchorOrigin="Origin.BottomLeft" TransformOrigin="Origin.TopLeft">
        @if (opt!.EnableEdit)
        {
            <MudMenuItem Icon="@Icons.Material.Filled.Edit"
                         OnClick="@(() => treeView.InvokeEditAsync(Node))">Редактировать</MudMenuItem>
        }
        @if (opt.EnableAddChild)
        {
            <MudMenuItem Icon="@Icons.Material.Filled.Add"
                         OnClick="@(() => treeView.InvokeAddChildAsync(Node))">Добавить дочерний</MudMenuItem>
        }
        @if (opt.EnableDelete)
        {
            <MudMenuItem Icon="@Icons.Material.Filled.Delete"
                         OnClick="@(() => treeView.InvokeDeleteAsync(Node))">Удалить</MudMenuItem>
        }
        @foreach (var item in opt.CustomMenuItems)
        {
            <MudMenuItem Icon="@item.Icon"
                         OnClick="@(() => treeView.InvokeCustomAsync(item, Node))">@item.Label</MudMenuItem>
        }
    </MudMenu>
}
```

## Шаг 4 — файл `Clayzor.Lib.Web.Controls/Components/Tree/ClayTreeView.Mutations.cs` (публичные обёртки)

`ClayTreeNodeView` — отдельный компонент; приватные методы `ClayTreeView` ему недоступны. Добавить
тонкие публичные (или internal) обёртки, вызываемые из разметки узла:

```csharp
public partial class ClayTreeView
{
    internal Task InvokeEditAsync(ClayTreeNode node)      => EditNodeAsync(node);
    internal Task InvokeAddChildAsync(ClayTreeNode node)  => AddChildAsync(node);
    internal Task InvokeDeleteAsync(ClayTreeNode node)    => DeleteNodeAsync(node);
    internal Task InvokeCustomAsync(ClayTreeMenuItem item, ClayTreeNode node) => ExecuteCustomAsync(item, node);
}
```

(`ClayTreeNodeView` уже приводит `Tree` к `ClayTreeView` — internal-методы видны внутри той же
сборки.)

## Шаг 5 — стили (`clay-tree.css`)

Кнопка меню появляется по наведению на строку (чтобы не засорять интерфейс):

```css
.clay-tree-node-menu { opacity: 0; transition: opacity .15s; margin-left: auto; }
.clay-tree-node-row:hover .clay-tree-node-menu { opacity: 1; }
.clay-tree-node--selected .clay-tree-node-menu { opacity: 1; }
```

## Критерии приёмки

- Меню отсутствует, если все `Enable*` = false и `CustomMenuItems` пуст.
- «Редактировать»: диалог с текущим названием и путём к ноде; «ОК» → `UpdateNodeAsync` → текст
  ноды перечитан из БД (уровень не перезагружался). «Обновить» перечитывает значение и путь из БД.
  «Отмена» ничего не меняет.
- «Добавить дочерний»: диалог с пустым полем и путём к РОДИТЕЛЮ; «ОК» → `AddChildAsync` →
  уровень родителя перезагружен, родитель раскрыт. Новая нода может быть не видна при активном
  фильтре/неполной подгрузке — это допустимо.
- «Удалить»: подтверждение «Вы уверены, что хотите удалить [Название]?»; при «Да» →
  `DeleteAsync` → уровень родителя перезагружен, выделение снято, если удаляли выделенный.
- Кастомный пункт вызывает свой делегат с текущим узлом.
