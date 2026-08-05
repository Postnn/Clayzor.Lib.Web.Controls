# CTM6 — Ядро обновления: перезагрузка затронутых уровней, сохранение раскрытости и фокуса

## Цель

После любой мутации нужно обновить из БД только затронутые уровни, сохранив раскрытость и
выделение остальных узлов. Здесь создаётся инфраструктура, которой пользуются CTM7 (меню) и
CTM8 (DnD): поиск узла по Id, перезагрузка уровня конкретного родителя, перечитывание текста
одного узла.

Опираемся на уже существующие механизмы компонента: ленивую загрузку уровня
(`EnsureChildrenLoadedAsync` / `LoadRootsAsync` из `ClayTreeView.Loading.cs`) и множество
раскрытых `_expanded`. Не дублировать их — переиспользовать.

## Шаг 1 — файл `Clayzor.Lib.Web.Controls/Components/Tree/ClayTreeView.Mutations.cs` (новый partial)

Создать новый partial-класс компонента. Он будет пополняться в CTM7 и CTM8 — здесь только базовая
инфраструктура обновления.

```csharp
using Clayzor.Lib.Web.Controls.Components.Tree.Models;

namespace Clayzor.Lib.Web.Controls.Components.Tree;

public partial class ClayTreeView
{
    // ── Поиск узлов в загруженном дереве ─────────────────────────────────────────

    /// <summary>
    /// Ищет узел по строковому Id в загруженном дереве (обход в глубину от корней).
    /// Возвращает null, если узел не загружен в память.
    /// </summary>
    private ClayTreeNode? FindNodeById(string id)
    {
        foreach (var root in _roots)
        {
            var found = FindInSubtree(root, id);
            if (found is not null) return found;
        }
        return null;
    }

    private static ClayTreeNode? FindInSubtree(ClayTreeNode node, string id)
    {
        if (node.Id == id) return node;
        foreach (var child in node.Children)
        {
            var found = FindInSubtree(child, id);
            if (found is not null) return found;
        }
        return null;
    }

    /// <summary>
    /// Находит родителя узла по его RawId родителя. null означает «корень»
    /// (родитель — псевдоуровень корней).
    /// </summary>
    private ClayTreeNode? FindParentNode(ClayTreeNode node)
        => node.Parent;

    // ── Перезагрузка уровня одного родителя ──────────────────────────────────────

    /// <summary>
    /// Перечитывает из БД детей указанного родителя, сохраняя раскрытость уже раскрытых
    /// потомков и текущее выделение. Если <paramref name="parent"/> == null — перезагружает
    /// корневой уровень.
    /// </summary>
    private async Task ReloadLevelAsync(ClayTreeNode? parent)
    {
        // Запомнить, какие дети были раскрыты (по Id), чтобы восстановить после перезагрузки.
        var previouslyExpanded = new HashSet<string>();
        var childrenBefore = parent?.Children ?? _roots;
        foreach (var ch in childrenBefore)
            if (ch.IsExpanded) previouslyExpanded.Add(ch.Id);

        if (parent is null)
        {
            // Корневой уровень.
            await LoadRootsAsync();   // существующий метод; он сам перечитывает корни
        }
        else
        {
            // Сбросить загрузку уровня и перезагрузить лениво.
            parent.IsLoaded = false;
            parent.Children.Clear();
            parent.LoadedAllChildren = true;
            parent.LastChildCursor = null;
            await EnsureChildrenLoadedAsync(parent);   // существующий метод ленивой загрузки
        }

        // Восстановить раскрытость тех детей, что снова присутствуют и были раскрыты.
        var childrenAfter = parent?.Children ?? _roots;
        foreach (var ch in childrenAfter)
        {
            if (previouslyExpanded.Contains(ch.Id) && ch.HasChildren)
            {
                ch.IsExpanded = true;
                _expanded.Add(ch.Id);
                await EnsureChildrenLoadedAsync(ch);
            }
        }

        StateHasChanged();
    }

    /// <summary>
    /// Перечитывает из БД текст ОДНОГО узла (после редактирования поля названия).
    /// Текст берётся из TextColumn через тот же источник, что грузит уровни.
    /// </summary>
    private async Task RefreshNodeTextAsync(ClayTreeNode node)
    {
        var newText = await ReadNodeTextAsync(node);   // см. шаг 2
        if (newText is not null)
        {
            node.Text = newText;
            StateHasChanged();
        }
    }
}
```

Замечания для агента:
- Имена существующих методов (`LoadRootsAsync`, `EnsureChildrenLoadedAsync`) и полей (`_roots`,
  `_expanded`) взять ТОЧНО как в текущем коде (`ClayTreeView.Loading.cs`,
  `ClayTreeView.Expansion.cs`, `ClayTreeView.razor.cs`). Если сигнатура
  `EnsureChildrenLoadedAsync` отличается (например, требует иных аргументов) — адаптировать вызов,
  не меняя сам метод.
- Если `EnsureChildrenLoadedAsync` уже проверяет `IsLoaded`/`HasChildren` и выходит раньше —
  сброс `IsLoaded=false` перед вызовом обязателен (сделано выше).

## Шаг 2 — файл `Clayzor.Lib.Web.Controls/Components/Tree/ClayTreeView.Mutations.cs` (тот же файл)

Добавить чтение текста одного узла. Реализация зависит от того, как компонент читает данные:
через `_dataSource` (см. `IClayTreeDataSource`) или напрямую. Универсальный способ —
перечитать строку узла тем же источником и взять `TextColumn`. Если у источника нет метода
«прочитать одну строку по Id», добавить его; минимально — выполнить точечный SELECT через тот же
`DbManager`, что использует источник.

Ориентировочная реализация (адаптировать под фактический источник данных):

```csharp
public partial class ClayTreeView
{
    /// <summary>
    /// Читает актуальный текст узла (TextColumn) из БД по его RawId.
    /// Возвращает null, если строка не найдена.
    /// </summary>
    private async Task<string?> ReadNodeTextAsync(ClayTreeNode node)
    {
        if (node.RawId is null) return null;

        var schema   = Options.Schema;
        var idCol    = schema.IdColumn;
        var textCol  = schema.TextColumn;
        var selectSql = Options.SelectSql;

        // Точечный запрос текста по первичному ключу.
        var sql = $"SELECT [{textCol}] AS Text FROM ({selectSql}) _src WHERE [{idCol}] = @id";

        var db = ResolveDb();   // существующий метод резолвинга DbManager (учитывает ConnectionStringName)
        var text = await db.QueryFirstOrDefaultAsync<string?>(sql, new { id = node.RawId });
        return text;
    }
}
```

Замечания:
- `textCol` может быть вычисляемым выражением с алиасом — тогда оборачивание `[...]` неверно.
  Проверить, как формируется `TextColumn` при обычной загрузке уровня (в
  `ClayTreeSqlBuilder`/источнике): использовать ТУ ЖЕ форму ссылки на текст, что и там, чтобы
  результат совпадал. Если там `SELECT ... AS [текст]` по алиасу — здесь тоже ссылаться на алиас.
- `ResolveDb()` и метод запроса (`QueryFirstOrDefaultAsync` или аналог из `DbManager`) взять
  фактические из проекта.

## Критерии приёмки

- `FindNodeById` находит загруженный узел и возвращает null для незагруженного.
- `ReloadLevelAsync(null)` перечитывает корни; `ReloadLevelAsync(parent)` перечитывает уровень
  parent; в обоих случаях ранее раскрытые дети остаются раскрытыми, если снова присутствуют.
- `RefreshNodeTextAsync` меняет только `node.Text`, не трогая структуру уровня.
- Выделение (`_selectedIds`) после `ReloadLevelAsync` не сбрасывается для узлов, оставшихся в
  дереве.
