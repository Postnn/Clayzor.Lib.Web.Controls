Исправь **только тест `FailedReinit_RetriesSameIdentity_OnSameComponentInstance`**. Production-код не трогать.

Текущий сценарий `A success → B fail → B retry` на том же `cut` теперь правильный, но тест всё ещё не соответствует acceptance criteria в двух местах.

## 1. Убрать оба `catch { }`

Сейчас есть:

```csharp
try
{
    cut.Render(...);
}
catch { }
```

Это сделано и для failed B, и для successful retry B.

Так нельзя.

Для **первой попытки B** необходимо явно доказать controlled exception:

```csharp
Assert.Throws<InvalidOperationException>(() =>
    cut.Render(p => p.Add(c => c.Options,
        new ClayGridOptions { Dynamic = true })));
```

Exception должен быть именно тот, который выдаёт:

```csharp
Script.Error(new InvalidOperationException("boom"))
```

После этого можно проверить:

```csharp
Assert.Null(instance.GetColumnMeta("ColumnA"));
```

Это доказывает, что reset A произошёл до failed initialization B.

Для **второй попытки B** никакого `try/catch` быть не должно:

```csharp
cut.Render(p => p.Add(c => c.Options,
    new ClayGridOptions { Dynamic = true }));
```

Если retry бросит exception, тест обязан упасть.

Не использовать:

```csharp
catch { }
catch (Exception)
try/catch с проглатыванием exception
```

---

## 2. Доказать ровно две попытки initialization B

Сейчас наличие `ColumnB` доказывает, что retry когда-то завершился успешно, но не доказывает acceptance criterion:

```text
B initialization attempts == 2
```

Нужен deterministic counter именно для **definition initialization Grid B**, отдельно от Grid A.

Не использовать общий слабый assert:

```csharp
Assert.True(DefCount > 1);
```

потому что Grid A уже выполнял definition query.

Используй существующую scripted DB infrastructure / `CommandLog` либо добавь минимальный test-only counter.

Должно быть доказано:

```text
Grid A definition attempts = 1

Grid B definition attempts:
    attempt #1 -> InvalidOperationException
    attempt #2 -> success

B attempts == 2
```

Предпочтительно:

```csharp
Assert.Equal(2, bDefinitionAttempts);
```

---

## Итоговый сценарий теста

Тест должен выглядеть концептуально так:

```text
Render A successfully
    ↓
save cut.Instance
    ↓
verify ColumnA exists
    ↓
navigate/change identity to B
    ↓
B definition attempt #1
    ↓
controlled InvalidOperationException
    ↓
Assert.Throws<InvalidOperationException>
    ↓
verify ColumnA already removed
    ↓
append successful B script
    ↓
render SAME cut with EXACT SAME B identity
    ↓
B definition attempt #2 succeeds
    ↓
Assert.Same(originalInstance, cut.Instance)
Assert ColumnB exists
Assert ColumnA absent
Assert bDefinitionAttempts == 2
```

Критически важно: между failed B и retry B **не менять**:

```text
GridId
CLID
sharedId
dynamic settings
```

Иначе это уже будет другая lifecycle identity.

## Acceptance criteria

* [ ] Production-код не изменён.
* [ ] Используется один `cut`.
* [ ] Используется один component instance.
* [ ] A успешно инициализируется.
* [ ] Первая B initialization бросает controlled `InvalidOperationException`.
* [ ] Exception проверяется через `Assert.Throws<InvalidOperationException>`.
* [ ] После failed B `ColumnA` отсутствует.
* [ ] Retry B выполняется без `try/catch`.
* [ ] Retry использует точно ту же identity B.
* [ ] `Assert.Same(instance, cut.Instance)` проходит.
* [ ] После retry `ColumnB` присутствует.
* [ ] `ColumnA` отсутствует.
* [ ] Доказано `bDefinitionAttempts == 2`.
* [ ] В тесте нет `catch { }`.
* [ ] В тесте нет blanket catch.
* [ ] Не используется reflection для lifecycle/key/reset.
* [ ] Не создаётся второй `ClayGrid`.
* [ ] Real SQL Server не используется.
* [ ] Все тесты проходят.

После исправления выполнить:

```bash
dotnet test
```

для `Clayzor.Lib.Web.Controls.Tests`.

Сделать **один отдельный test-only commit**.

Рекомендуемый message:

```text
CGFR1.2: tighten same-instance failed reinit regression
```

В отчёте напиши:

1. как теперь проверяется `InvalidOperationException`;
2. как считается именно количество B definition attempts;
3. подтверждение `bDefinitionAttempts == 2`;
4. подтверждение, что retry идёт на том же `cut`/instance;
5. результат `dotnet test`.
