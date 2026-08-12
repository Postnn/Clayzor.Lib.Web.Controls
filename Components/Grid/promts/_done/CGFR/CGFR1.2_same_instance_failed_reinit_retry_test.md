# CGFR1.2 — Dynamic ClayGrid: regression test failed init → retry на том же component instance

## ВАЖНО

Это очень узкий corrective task после CGFR1.1.

Production-код CGFR1.1 уже принят.

НЕ менять production-код без обнаружения нового реального дефекта.

Исправить только один недостаток тестового покрытия:

> существующий `InitException_AllowsRetry` создаёт новый компонент после failed init и поэтому не доказывает retry той же identity на том же component instance.

---

# 1. Что уже правильно

Production semantics после CGFR1.1:

```csharp
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

Это правильно.

Existing bUnit tests также уже покрывают:

```text
A -> B same instance
same-key no reinit
URI-aware query
same mutable Options instance
dynamic -> static
```

Не переделывать их без необходимости.

---

# 2. Проблема текущего retry test

Сейчас test примерно такой:

```csharp
Assert.Throws<InvalidOperationException>(() =>
    _ctx.Render<ClayGrid<ClayDynamicRow>>(...));

var cut = _ctx.Render<ClayGrid<ClayDynamicRow>>(...);
```

То есть проверяется:

```text
component #1
init B failed

component #2
same identity B
init succeeds
```

Это НЕ доказывает CGFR1.1 bug fix.

Даже старый неправильный production-код:

```csharp
_currentDynamicKey = key;
await InitDynamicMode(); // throws
```

прошёл бы такой test, потому что второй компонент создаётся с:

```text
_currentDynamicKey = null
```

---

# 3. Требуемый regression scenario

Нужен один живой rendered component.

Сценарий:

```text
1. Render Grid A успешно.
2. Сохранить cut.Instance.
3. Переключить SAME cut на Grid B.
4. Первая initialization B бросает controlled exception.
5. Проверить, что exception реально вышел наружу.
6. Подготовить success script для B.
7. Повторно render SAME cut с той же identity B.
8. B успешно инициализируется.
9. cut.Instance тот же.
```

---

# 4. Почему нужен предварительный successful A

Не начинать тест с failed initial render B.

Если initial render компонента падает, bUnit может считать render неуспешным и дальнейшая работа с тем же rendered component становится неоднозначной.

Надёжный scenario:

```text
A success
-> B failure
-> B retry success
```

Так компонент гарантированно уже существует и был успешно отрендерен до failure.

---

# 5. Обязательный SAME INSTANCE assert

После initial A:

```csharp
var instance = cut.Instance;
```

После failed B и успешного retry B:

```csharp
Assert.Same(instance, cut.Instance);
```

Нельзя создавать второй:

```csharp
_ctx.Render<ClayGrid<...>>(...)
```

после failed B.

---

# 6. Controlled DB script

Использовать существующую infrastructure:

```text
ScriptedConnection
_globalQueue
BuildInitScript(...)
```

Не подключаться к real SQL Server.

Пример последовательности:

### A

Полный success script.

### B first attempt

Первый definition load:

```text
throws InvalidOperationException("boom")
```

### B second attempt

Полный success script B.

---

# 7. Не очищать состояние способом, который скрывает bug

Допускается очистить остаток `_globalQueue` после controlled failed B, если failure происходит на первой DB operation B.

Но НЕ трогать:

```text
_currentDynamicKey
component private fields
ResetDynamicRuntimeState
```

из test reflection.

Иначе test перестанет доказывать production lifecycle.

---

# 8. Expected exception

Использовать конкретный assert:

```csharp
Assert.Throws<InvalidOperationException>(() =>
    cut.Render(... B ...));
```

или актуальный bUnit equivalent.

Не использовать blanket catches.

---

# 9. Retry должен быть той же identity

После failed B повторный render должен использовать ТОЧНО тот же lifecycle key B:

```text
same GridId
same CLID
same sharedId
same dynamic settings
```

Не менять URL/Options между failed B и retry B.

---

# 10. Что проверить после failed B

После exception полезно проверить observable reset:

```text
ColumnA отсутствует
```

Потому что `ResetDynamicRuntimeState()` должен выполниться ДО `InitDynamicMode(B)`.

Если bUnit после thrown render позволяет безопасно читать instance state.

Если нет — этот assert optional.

---

# 11. Что проверить после successful retry B

Обязательно:

```text
ColumnB present
ColumnA absent
same component instance
```

Желательно также:

```text
B initialization attempts == 2
```

---

# 12. Нужен счётчик именно B attempts

Сделать deterministic proof.

Например через `CommandLog` или отдельный test counter.

Ожидание:

```text
B initialization attempts == 2
```

Не просто `> 1`.

---

# 13. Test name

Переименовать существующий test во что-то явно отражающее контракт.

Например:

```csharp
FailedReinit_RetriesSameIdentity_OnSameComponentInstance()
```

---

# 14. Старый test удалить/заменить

Существующий `InitException_AllowsRetry`, который создаёт два компонента, удалить или полностью переписать.

---

# 15. Не использовать reflection

CGFR1.2 не должен добавлять reflection для:

```text
_currentDynamicKey
OnParametersSetAsync
ResetDynamicRuntimeState
```

Primary proof должен быть полностью через bUnit lifecycle.

---

# 16. Не менять production

В production repo не должно быть изменений, кроме, возможно, `AGENTS.md`/prompt archival.

Если agent считает, что production надо менять — сначала доказать новым failing test реальный production defect.

---

# 17. AGENTS.md

Добавить короткую запись:

```text
CGFR1.2:
failed reinitialization retry покрыт bUnit regression:
A success -> B failure -> same B retry success
на том же rendered component instance.
```

---

# 18. Acceptance criteria

CGFR1.2 принимается только если:

- [ ] Production lifecycle code CGFR1.1 не изменён без новой причины.
- [ ] Existing `InitException_AllowsRetry` заменён/переписан.
- [ ] Initial Grid A успешно отрендерен.
- [ ] Сохранён `cut.Instance`.
- [ ] Первая reinit Grid B бросает controlled `InvalidOperationException`.
- [ ] Retry выполняется на том же `cut`.
- [ ] Retry использует ту же identity B.
- [ ] После retry Grid B успешно инициализирован.
- [ ] `Assert.Same(instance, cut.Instance)` проходит.
- [ ] `ColumnB` присутствует.
- [ ] `ColumnA` отсутствует.
- [ ] Доказано ровно 2 B initialization attempts.
- [ ] Нет нового component render после failed B.
- [ ] Нет reflection lifecycle invocation.
- [ ] Нет blanket catches.
- [ ] Нет real SQL Server dependency.
- [ ] Все existing tests green.

---

# 19. Проверка

Перед завершением:

```bash
dotnet test
```

для `Clayzor.Lib.Web.Controls.Tests`.

В отчёте указать:

1. имя переписанного test;
2. как устроен A success -> B fail -> B retry;
3. как доказан same component instance;
4. как доказано ровно 2 B attempts;
5. test result.

---

# 20. Коммит

Один отдельный commit только для CGFR1.2.

Предлагаемый message:

```text
CGFR1.2: verify failed dynamic reinit retries on same component
```

Не смешивать CGFR2 и другие исправления.
