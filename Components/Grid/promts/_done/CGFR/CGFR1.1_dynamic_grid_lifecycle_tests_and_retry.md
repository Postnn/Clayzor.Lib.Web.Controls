# CGFR1.1 — Dynamic ClayGrid: исправить failure semantics lifecycle key и заменить pseudo-lifecycle tests на реальные bUnit tests

## ВАЖНО

Это узкий corrective task после CGFR1.

CGFR1 уже принят по архитектуре:

- `ClayGridDynamicKey` введён;
- key value-based;
- учитываются `GridId`, `CLID`, `sharedId` и relevant dynamic settings;
- `Query` стал URI-aware;
- dynamic initialization перенесена в `OnParametersSetAsync`;
- `_dynamicInitDone` удалён;
- `ResetDynamicRuntimeState()` введён;
- reset очищает definition-dependent runtime;
- same-key path не делает reinit;
- контракт проекта: на странице максимум один `ClayGrid` в Dynamic mode.

Не переделывать это заново.

Исправить ТОЛЬКО два дефекта:

1. `_currentDynamicKey` сейчас фиксируется до успешного завершения `InitDynamicMode()`;
2. lifecycle tests сейчас не являются реальными bUnit component tests и проверяют reflection/NRE path вместо реального Blazor lifecycle.

---

# 1. Production bug: key фиксируется слишком рано

Сейчас структура примерно такая:

```csharp
var key = ClayGridDynamicKey.Create(...);

if (key == _currentDynamicKey)
    return;

_currentDynamicKey = key;
ResetDynamicRuntimeState();
await InitDynamicMode();
```

Проблема:

```text
identity B detected
_currentDynamicKey = B
reset
InitDynamicMode(B) throws unexpected exception
same component rerenders with B
key == _currentDynamicKey
=> return
=> InitDynamicMode(B) больше не запускается
```

Компонент может навсегда остаться:

```text
в reset/partial state
с key B
без завершённой initialization B
```

Это неправильная failure semantics.

---

# 2. Требуемая semantics key commit

Lifecycle key должен означать:

```text
"эта identity уже обработана до terminal result"
```

а не:

```text
"мы начали попытку initialization"
```

Для успешной initialization:

```text
reset
init
commit key
```

Для terminal UI-result внутри `InitDynamicMode()`:

```text
GridId отсутствует
definition not found
invalid shared link
```

если метод завершился НОРМАЛЬНО и выставил `_dynamicError`, identity можно считать обработанной.

Но если `InitDynamicMode()` БРОСИЛ exception наружу:

```text
_currentDynamicKey НЕ должен оставаться B
```

Следующий parameter-set той же identity должен иметь возможность повторить initialization.

---

# 3. Предпочтительная production structure

Не обязательно копировать буквально, но semantics должна быть такой:

```csharp
var key = ClayGridDynamicKey.Create(...);

if (key == _currentDynamicKey)
    return;

ResetDynamicRuntimeState();

try
{
    await InitDynamicMode();
    _currentDynamicKey = key;
}
catch
{
    _currentDynamicKey = null;
    throw;
}
```

или эквивалент.

Важно:

```text
normal completion of InitDynamicMode
=> key committed

thrown exception
=> key not committed
```

---

# 4. Не ломать terminal `_dynamicError` path

Существующий `InitDynamicMode()` может завершиться без exception, но с:

```text
_dynamicError != null
```

Например:

```text
не указан GridId
grid definition не найден
invalid shared link
```

В таких случаях НЕ нужно автоматически reinitialize на каждый rerender.

Если `InitDynamicMode()` завершился normally:

```text
_currentDynamicKey = key
```

даже если UI показывает terminal error.

---

# 5. Reset key order

При identity A -> B:

```text
old key A
new key B
```

до initialization B старое runtime state должно быть сброшено.

Допустимая последовательность:

```text
detect B
ResetDynamicRuntimeState()
attempt InitDynamicMode(B)
```

Не добавлять отдельные pending-key state machines без необходимости.

---

# 6. Dynamic true -> false

Существующий CGFR1 behavior сохранить:

```text
Dynamic true -> false
=> old dynamic runtime reset
=> _currentDynamicKey = null
```

Не менять static mode.

---

# 7. Главная проблема тестов

Сейчас `ClayGridReinitTests`:

- создаёт компонент через `ActivatorUtilities.CreateInstance`;
- руками inject'ит private fields reflection;
- вручную вызывает `OnParametersSetAsync` reflection;
- не inject'ит `Db`;
- ожидает `NullReferenceException` из `InitDynamicMode`;
- вручную выставляет `_currentDynamicKey`.

Например path вида:

```csharp
try
{
    await CallOnParamsSetAsync(grid);
}
catch (NullReferenceException)
{
}
```

НЕ является component lifecycle regression.

Это проверяет implementation details, а не Blazor behavior.

---

# 8. CGFR1.1 требует реальные bUnit tests

В test project уже установлен bUnit.

Нужно использовать настоящий renderer:

```text
TestContext / BunitContext
RenderComponent<ClayGrid<T>>(...)
SetParametersAndRender(...)
```

или актуальный API bUnit 2.x.

Ключевой тест должен реально пройти через:

```text
Blazor parameter lifecycle
render
rerender same component instance
```

Без прямого вызова:

```text
OnParametersSetAsync()
ResetDynamicRuntimeState()
```

через reflection.

---

# 9. SAME component instance test — обязателен

Сценарий:

```text
render Dynamic Grid A
capture cut.Instance

change parameters / URL to Dynamic Grid B
SetParametersAndRender on SAME cut

Assert.Same(originalInstance, cut.Instance)
```

Далее доказать:

```text
B initialization happened
A runtime state gone
```

Нельзя создавать второй component.

---

# 10. Нужен real initialization seam без real SQL Server

Проблему тестируемости решить НЕ через `Db = null`.

Использовать test seam.

Предпочтение:

1. internal abstraction / injectable loader;
2. internal delegate seam;
3. internal virtual method;
4. fake `DbManager`/`DbConnection` infrastructure, если уже удобно.

Не добавлять public test-only API.

---

# 11. Test 1 — A -> B same instance

Обязательный.

Grid A:

```text
GridId = 101
definition Title = A
columns = [ColumnA]
```

Grid B:

```text
GridId = 202
definition Title = B
columns = [ColumnB]
```

Сценарий:

```csharp
var cut = ctx.RenderComponent<ClayGrid<...>>(parameters for A);
var instance = cut.Instance;

cut.SetParametersAndRender(parameters for B);

Assert.Same(instance, cut.Instance);
```

Доказать observable state:

```text
B active
ColumnB exists
ColumnA absent
```

Если DOM assertion сложен, допустим internal read-only test seam через `InternalsVisibleTo`, но execution всё равно bUnit.

---

# 12. Test 2 — same key no reinit

Обязательный.

Считать number of definition loads:

```text
render A
initialization count = 1

rerender SAME component
identity unchanged
presentation-only option changed

initialization count remains 1
```

Нельзя вручную выставлять `_currentDynamicKey`.

Key должен быть выставлен production lifecycle самостоятельно.

---

# 13. Test 3 — URI-aware query on same rendered component

Обязательный.

`Options.DynamicGridId = null`.

Initial URL:

```text
?id=101&CLID=1
```

Render.

Затем через test `NavigationManager`:

```text
?id=202&CLID=2
```

и rerender SAME component.

Ожидание:

```text
resolved GridId = 202
resolved CLID = 2
initialization B happened
```

Это одновременно доказывает:

```text
Query cache not stale
lifecycle key changed
```

Не тестировать только property `Query` reflection.

---

# 14. Test 4 — same mutable Options instance

Обязательный.

```csharp
var options = new ClayGridOptions
{
    Dynamic = true,
    DynamicGridId = 101
};

var cut = Render(... options ...);

options.DynamicGridId = 202;

cut.SetParametersAndRender(... SAME options instance ...);
```

Ожидание:

```text
reinit happened
```

Это доказывает value-based key вместо reference equality.

---

# 15. Test 5 — init exception allows retry

ОБЯЗАТЕЛЬНЫЙ новый regression.

Controlled loader:

```text
first initialization for GridId 101:
    throws InvalidOperationException("boom")

second initialization same GridId 101:
    succeeds
```

Сценарий:

```text
first render / parameter set -> exception propagates
next lifecycle attempt SAME identity
-> initialization runs again
-> succeeds
```

Проверить:

```text
attempt count == 2
```

Это главный regression для production bug CGFR1.1.

---

# 16. Не использовать NullReferenceException как test control flow

Удалить/переписать tests, где intentionally отсутствует `Db`.

Не должно быть:

```csharp
catch (NullReferenceException) { }
```

Не должно быть:

```csharp
catch { }
```

для lifecycle tests.

Expected exception должен быть конкретным и контролируемым:

```csharp
await Assert.ThrowsAsync<InvalidOperationException>(...)
```

или bUnit equivalent.

---

# 17. Test reset secondary

Unit test `ResetDynamicRuntimeState_ClearsAllFields` можно оставить как secondary white-box test.

Но:

```text
он НЕ заменяет bUnit lifecycle test
```

Если reflection test слишком хрупкий и bUnit A->B уже observable доказывает очищение, его можно упростить или удалить.

---

# 18. Key unit tests

`ClayGridDynamicKeyTests` в целом оставить.

Они полезны.

Не надо переписывать equality tests без причины.

---

# 19. Не делать full MudBlazor UI test, если не нужно

Не обязательно проверять весь `MudDataGrid` DOM.

Цель — lifecycle.

Если MudBlazor rendering требует registrations:

```text
AddMudServices()
AddMudExtensions()
JSInterop.Mode = Loose
```

настроить test context корректно.

Не объявлять bUnit «невозможным» и не обходить renderer.

---

# 20. Fake NavigationManager

Если bUnit уже предоставляет `FakeNavigationManager`, использовать его.

Не писать второй кастомный `FakeNavigationManager`, если не нужен.

---

# 21. Observable lifecycle counters

Допустимо добавить internal test seam:

```text
definition load count
loaded GridId
loaded columns
```

Но не public.

Предпочтительно fake loader сам хранит:

```csharp
List<int> LoadedGridIds
```

и test asserts:

```text
[101, 202]
```

---

# 22. Failure semantics test detail

Очень важно, чтобы первый failed init реально прошёл через production:

```text
OnParametersSetAsync
ResetDynamicRuntimeState
InitDynamicMode
throw
```

После первого exception НЕ выставлять key вручную.

Следующий lifecycle invocation той же identity должен сам вызвать init второй раз.

---

# 23. Key commit implementation test

После успешной init:

```text
same-key rerender
=> no second init
```

После failed init:

```text
same-key rerender
=> retry init
```

Эти два теста вместе полностью доказывают правильный момент commit key.

---

# 24. Не менять CGFR1 reset scope

Не расширять `ResetDynamicRuntimeState()` без найденной причины.

CGFR1.1 не должен превращаться в новый refactor.

Если bUnit test обнаружит конкретное stale поле — исправить именно его и задокументировать.

---

# 25. Не менять DynamicSql semantics

Не трогать SQL behavior ради тестов.

CGFR1.1 не должен добавлять новые изменения SQL.

---

# 26. Не смешивать CGFR2

Не исправлять сейчас:

```text
blanket catch в shared params
lookup exception boundaries
JS exception boundaries
```

Это отдельные задачи.

CGFR1.1 только:

```text
key failure semantics
real component lifecycle tests
```

---

# 27. AGENTS.md

Обновить `Components/Grid/AGENTS.md` коротко:

```text
CGFR1.1:
_currentDynamicKey коммитится только после normal completion InitDynamicMode.
Thrown initialization exception не блокирует retry той же identity.

Lifecycle behavior покрыт реальными bUnit same-instance tests.
```

Не писать, что reflection tests являются component tests.

---

# 28. Acceptance criteria

CGFR1.1 принимается только если:

## Production

- [ ] `_currentDynamicKey` не фиксируется до normal completion `InitDynamicMode()`.
- [ ] Thrown exception не оставляет identity committed.
- [ ] Same identity после failed init может retry.
- [ ] Normal terminal `_dynamicError` result не вызывает endless retry.
- [ ] Same-key после success не reinit.
- [ ] Dynamic -> static behavior сохранён.

## Tests

- [ ] Есть настоящий bUnit rendered `ClayGrid`.
- [ ] SAME component instance A -> B доказан `Assert.Same`.
- [ ] B реально инициализирован.
- [ ] A runtime state отсутствует.
- [ ] Same-key rerender не reinit.
- [ ] URL `id/CLID` change на same component вызывает B init.
- [ ] Same mutable Options instance change ловится.
- [ ] Failed init -> same identity retry -> success.
- [ ] Нет ожидаемого `NullReferenceException`.
- [ ] Нет blanket `catch { }` в lifecycle tests.
- [ ] Нет real SQL Server dependency.

## Hygiene

- [ ] `ClayGridDynamicKeyTests` остаются зелёными.
- [ ] Existing Grid tests green.
- [ ] Existing Tree/DALC tests green.
- [ ] `AGENTS.md` обновлён.
- [ ] Нет public test-only API.

---

# 29. Проверка

Перед завершением:

```bash
dotnet test
```

для `Clayzor.Lib.Web.Controls.Tests`.

Также:

```bash
dotnet build
```

основной `Clayzor.Lib.Web.Controls`.

В отчёте указать:

1. production files;
2. test files;
3. новый порядок key commit;
4. как fake initialization работает без SQL Server;
5. имя bUnit A->B test;
6. имя failed-init retry test;
7. доказательство same-key no-reinit;
8. build/test results.

---

# 30. Коммит

Один отдельный commit.

Предлагаемый message:

```text
CGFR1.1: fix dynamic init retry and add real bUnit lifecycle tests
```

Не смешивать другие audit fixes.
