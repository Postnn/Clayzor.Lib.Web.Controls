# CTMF5 — финальная корректировка обработки исключений CTM

## Цель

Завершить исправление обработки исключений в операциях изменения `ClayTree`.

После CTMF4 область `catch` была сужена относительно `ReloadLevelAsync`/`RefreshNodeTextAsync`, но остались две проблемы:

1. в `ClayTreeView.Mutations.cs` SQL-операции всё ещё обёрнуты в `catch (Exception)`, хотя `DbManager` обрабатывает через `ISqlErrorHandler` именно `Microsoft.Data.SqlClient.SqlException`;
2. в `ClayTreeDragDrop.cs` `DoReparentAsync` и `DoReorderAsync` по-прежнему имеют широкий `catch`, который охватывает одновременно SQL-мутацию и последующий reload/UI-state.

Нужно исправить это без изменения поведения успешных операций.

Выполнить **два шага строго по порядку**.

---

# CTMF5.1 — точная обработка SQL-ошибок в ClayTreeView.Mutations.cs

## Изменяемый файл

Только:

`Components/Tree/ClayTreeView.Mutations.cs`

Связанные `AGENTS.md` и документацию можно обновить перед коммитом согласно правилам проекта.

Не изменять другие исходные файлы.

---

## Фактический контракт DbManager

Перед правкой учитывать существующее поведение `Clayzor.Lib.DALC.DbManager`.

`DbManager`:

- ловит `Microsoft.Data.SqlClient.SqlException`;
- передаёт её в `ISqlErrorHandler`;
- connectivity-ошибки может поглотить внутри `DbManager` и вернуть `default`;
- обычные SQL-ошибки после `ISqlErrorHandler` повторно бросает;
- произвольные `Exception` как SQL-ошибки не обрабатывает.

Следовательно конструкция:

```csharp
catch (Exception)
{
    return;
}
```

с комментарием:

```text
ошибка сохранена ISqlErrorHandler
```

некорректна.

Она скрывает программные ошибки:

```text
NullReferenceException
InvalidOperationException
ArgumentException
ошибки кастомного IClayTreeMutations
```

которые `ISqlErrorHandler` вообще мог не видеть.

---

## Что изменить

Добавить:

```csharp
using Microsoft.Data.SqlClient;
```

если его ещё нет.

В методах:

```text
EditNodeAsync
AddChildAsync
DeleteNodeAsync
BuildPathAsync
```

заменить blanket:

```csharp
catch (Exception)
```

на:

```csharp
catch (SqlException)
```

Только эти SQL-исключения считаются уже переданными в `ISqlErrorHandler`.

---

## EditNodeAsync

Текущее смысловое поведение должно остаться:

```text
UpdateNodeAsync
    если SqlException -> прекратить операцию
    если успех -> RefreshNodeTextAsync
```

То есть концептуально:

```csharp
await RunBusyAsync("Сохранение…", async () =>
{
    try
    {
        await Mutations.UpdateNodeAsync(...);
    }
    catch (SqlException)
    {
        return;
    }

    await RefreshNodeTextAsync(node);
});
```

Не помещать `RefreshNodeTextAsync` в SQL-catch.

Если `RefreshNodeTextAsync` бросит исключение — оно должно выйти наружу.

---

## AddChildAsync

Требуемый порядок:

```text
Mutations.AddChildAsync

если SqlException:
    выйти из callback
    НЕ менять parent.HasChildren
    НЕ менять IsExpanded
    НЕ менять _expanded
    НЕ вызывать ReloadLevelAsync

если SQL успешен:
    существующая логика parent.HasChildren
    существующее раскрытие parent
    ReloadLevelAsync(parent)
```

Все ошибки после успешного SQL должны быть видимы и не должны интерпретироваться как SQL-ошибки.

---

## DeleteNodeAsync

Требуемый порядок:

```text
Mutations.DeleteAsync

если SqlException:
    выйти
    НЕ менять selection
    НЕ reload

если успех:
    _selectedIds.Remove(...)
    ReloadLevelAsync(parent)
```

Если reload падает после успешного DELETE, это исключение не проглатывать.

---

## BuildPathAsync

Путь остаётся необязательным.

Допустимо:

```csharp
try
{
    return await Mutations.GetNodePathAsync(...);
}
catch (SqlException)
{
    return null;
}
```

Не ловить здесь:

```csharp
Exception
InvalidOperationException
ArgumentException
NullReferenceException
```

Если кастомная реализация `IClayTreeMutations` содержит программную ошибку — её нельзя маскировать как отсутствие пути.

---

## Не менять

Не изменять:

- `ClayTreeDragDrop.cs`;
- `DbManager`;
- `ISqlErrorHandler`;
- `ClaySqlTreeMutations`;
- `IClayTreeMutations`;
- SQL;
- DnD;
- reload;
- UI;
- сигнатуры методов;
- `TableName`.

---

## Проверка CTMF5.1

Выполнить:

```bash
dotnet build
```

Если есть тесты дерева — выполнить.

Проверить логически:

### Обычная SQL-ошибка

```text
Mutations.UpdateNodeAsync -> SqlException
```

Результат:

```text
ISqlErrorHandler уже получил ошибку
RefreshNodeTextAsync не вызывается
исключение не роняет UI-операцию
```

### Connectivity error

Если `DbManager` сам поглотил connectivity error и вернул управление, компонент не должен создавать дополнительную ошибку.

Не добавлять отдельную connectivity-логику в `ClayTreeView`.

### Программная ошибка кастомного IClayTreeMutations

```text
Mutations.UpdateNodeAsync -> InvalidOperationException
```

Результат:

```text
исключение НЕ проглатывается
```

### Reload после успешного SQL падает

Результат:

```text
исключение НЕ проглатывается
```

---

## Отчёт CTMF5.1

Сообщить:

1. добавлен ли `using Microsoft.Data.SqlClient`;
2. какие `catch (Exception)` заменены;
3. какие операции теперь ловят `SqlException`;
4. результат сборки;
5. результат тестов.

После успешного CTMF5.1 перейти к CTMF5.2.

---

# CTMF5.2 — точная обработка исключений DnD

## Изменяемый файл

Только:

`Components/Tree/ClayTreeDragDrop.cs`

Связанные `AGENTS.md` и документацию можно обновить перед коммитом согласно правилам проекта.

Не изменять другие исходные файлы.

---

## Текущая проблема

Сейчас `DoReparentAsync` и `DoReorderAsync` имеют конструкцию примерно такого вида:

```csharp
try
{
    await RunBusyAsync("Перемещение…", async () =>
    {
        await Mutations.ReparentAsync(...);

        await ReloadLevelAsync(...);
        RestoreFocus(...);
    });
}
catch (JSDisconnectedException) { }
catch (ObjectDisposedException) { }
catch (InvalidOperationException) { }
catch (Exception)
{
    // ошибка уже сохранена ISqlErrorHandler
}
```

Это неверно.

`catch (Exception)` охватывает не только SQL-мутацию, но также:

```text
ReloadLevelAsync
FindNodeById
RestoreFocus
StateHasChanged
```

Если SQL успешно изменил БД, а reload упал, ошибка будет молча проглочена.

Кроме того:

```csharp
catch (InvalidOperationException)
{
    /* prerendering / нет JS */
}
```

тоже слишком широкий вокруг всей операции.

`InvalidOperationException` может быть обычной программной ошибкой и не должен автоматически трактоваться как JS lifecycle.

---

## Требуемая архитектура

Разделить обработку:

```text
1. confirmation
2. RunBusyAsync
3. внутри callback:
       SQL mutation — отдельный try/catch(SqlException)
       если SqlException -> return
       после успешного SQL:
           reload
           state restore
4. reload/state exceptions наружу
```

---

## Добавить using

Добавить:

```csharp
using Microsoft.Data.SqlClient;
```

если его ещё нет.

Существующий:

```csharp
using Microsoft.JSInterop;
```

оставить, потому что файл действительно использует JS interop в `OnDragOverAsync`.

---

## DoReparentAsync

Сохранить существующую логику CTMF3:

```text
oldParent
oldParentId
newParentId
draggedId
sameParent
freshNewParent через FindNodeById
root special case
```

Её не менять.

Изменить только обработку ошибки мутации.

Требуемый смысл:

```csharp
await RunBusyAsync("Перемещение…", async () =>
{
    try
    {
        await Mutations.ReparentAsync(...);
    }
    catch (SqlException)
    {
        return;
    }

    await ReloadLevelAsync(oldParent);

    if (!sameParent)
    {
        ...
        await ReloadLevelAsync(...);
    }

    RestoreFocus(draggedId);
});
```

После успешного `ReparentAsync` никакой blanket catch не должен скрывать ошибки reload/state.

---

## DoReorderAsync

Аналогично.

Сохранить:

```text
IsReorderNoOp
ConfirmAsync
ComputeNewLeft
ReloadLevelAsync
RestoreFocus
```

Только SQL должен быть внутри:

```csharp
try
{
    await Mutations.ReorderAsync(...);
}
catch (SqlException)
{
    return;
}
```

После успешного SQL:

```csharp
await ReloadLevelAsync(...);
RestoreFocus(...);
```

должны выполняться вне подавляющего catch.

---

## JS lifecycle exceptions

В `DoReparentAsync` и `DoReorderAsync` после реструктуризации не должно оставаться blanket:

```csharp
catch (JSDisconnectedException)
catch (ObjectDisposedException)
catch (InvalidOperationException)
catch (Exception)
```

вокруг всей операции.

Эти методы после открытия confirm-dialog непосредственно JS interop не вызывают.

Если конкретный вызов реально требует обработки JS lifecycle — catch должен стоять только вокруг этого конкретного вызова, а не вокруг SQL + reload + state.

Не добавлять новые generic-catch.

---

## OnDragOverAsync

Не менять существующую обработку:

```csharp
catch (JSDisconnectedException)
catch (ObjectDisposedException)
catch (InvalidOperationException)
```

в `OnDragOverAsync`.

Там непосредственно выполняется:

```csharp
JS.InvokeAsync(...)
```

и это отдельная JS-boundary.

Не добавлять:

```csharp
catch (NullReferenceException)
catch (Exception)
```

---

## Очень важный сценарий

Проверить:

```text
ReparentAsync успешно изменил БД
ReloadLevelAsync(oldParent) бросил исключение
```

Результат должен быть:

```text
исключение выходит наружу
не трактуется как SQL error
не проглатывается
```

То же для reorder.

---

## Кастомный IClayTreeMutations

Если пользователь зарегистрировал свою реализацию и она бросила:

```csharp
InvalidOperationException
ArgumentException
NullReferenceException
```

эти исключения нельзя проглатывать.

Ловить только:

```csharp
SqlException
```

---

## Не менять

Не изменять:

- `ClayTreeView.Mutations.cs`;
- CTMF1 no-op;
- CTMF2 deep expanded restore;
- CTMF3 stale newParent fix;
- `ComputeNewLeft`;
- `IsDropAllowedAsync`;
- SQL;
- `ClaySqlTreeMutations`;
- `DbManager`;
- `ISqlErrorHandler`;
- CSS;
- JS;
- диалоги;
- `TableName`.

---

## Проверка CTMF5.2

Выполнить:

```bash
dotnet build
```

Выполнить существующие тесты дерева.

Проверить:

### Reparent SQL error

```text
Mutations.ReparentAsync -> SqlException
```

Результат:

```text
reload старого уровня не выполняется
reload нового уровня не выполняется
RestoreFocus не выполняется
```

### Reorder SQL error

```text
Mutations.ReorderAsync -> SqlException
```

Результат:

```text
ReloadLevelAsync не выполняется
RestoreFocus не выполняется
```

### Reparent SQL success + reload error

```text
SQL success
ReloadLevelAsync -> Exception
```

Результат:

```text
Exception НЕ проглатывается
```

### Reorder SQL success + reload error

Результат:

```text
Exception НЕ проглатывается
```

### Custom mutation programming error

```text
Mutations.ReparentAsync -> InvalidOperationException
```

Результат:

```text
Exception НЕ проглатывается
```

### CTMF1 regression

Проверить:

```text
B before C -> no-op
B after A  -> no-op
```

Никакой SQL и confirm-dialog.

### CTMF3 regression

Проверить:

```text
P1 -> P2
P1 -> root
root -> P2
```

Используется актуальный экземпляр `newParent`.

---

# Финальная проверка CTMF-пакета

После CTMF5.1 и CTMF5.2:

```bash
dotnet build
```

Запустить все существующие тесты дерева.

Убедиться, что:

```text
CTMF1 — no-op reorder
CTMF2 — глубокое восстановление expanded
CTMF3 — отсутствие stale newParent
CTMF4/5 — SQL catch не скрывает reload/programming errors
```

работают одновременно.

---

# Финальный отчёт

Сообщить:

1. какие два исходных файла изменены;
2. сколько `catch (Exception)` удалено;
3. где теперь ловится `SqlException`;
4. остались ли blanket-catch вокруг DnD мутаций;
5. результат `dotnet build`;
6. результат тестов;
7. были ли найдены расхождения с промтом.

Не выполнять дополнительный рефакторинг.
Не менять контракт CTM.
Не менять `TableName`.
Не изменять SQL.