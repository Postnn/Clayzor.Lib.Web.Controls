# F11. Колоночное условие с двумя клаузами: показывать оба в форме фильтра

Файлы: `Components/Grid/Filter/ClayFilterGroup.razor.cs`,
`Components/Grid/Filter/ClayFilterDescriptionBuilder.cs`.

## Суть
Диалог колонки позволяет задать до двух условий на колонку (`ColumnFilter` со вторым
клаузом). В панели грида они показываются оба («Тип: содержит «прот» И содержит «моч»»),
и **в SQL используются оба** (`ClayCompositeSqlBuilder.BuildLeaf` учитывает
`HasSecondClause`). Но в форме настраиваемого фильтра read-only блок
«Редактируется в диалоге колонки: …» показывает **только первое** условие.

То есть это **баг отображения**, а не фильтрации: `GetLeafDescription` в
`ClayFilterGroup` берёт лишь `leaf.Operator`/`leaf.Value` и игнорирует второе условие.

## Исправить (и убрать дублирование)
Логика описания листа с двумя клаузами уже есть в `ClayFilterDescriptionBuilder`
(приватный `BuildLeafText`). Переиспользовать её вместо отдельной реализации.

1. В `ClayFilterDescriptionBuilder` открыть публичный метод описания одного листа:
   ```csharp
   /// <summary>Текст одного листа с обоими условиями (клаузами), напр.
   /// «Тип: содержит «прот» И содержит «моч»».</summary>
   public static string DescribeLeaf(ColumnFilter leaf, Func<string, string> getDisplayName)
       => BuildLeafText(leaf, getDisplayName);   // BuildLeafText уже учитывает HasSecondClause
   ```
   (Если `BuildLeafText` статический приватный — оставить приватным, а наружу выставить
   только `DescribeLeaf`.)

2. В `ClayFilterGroup.razor.cs` заменить тело `GetLeafDescription` на вызов общего метода
   с разрешением DisplayName из `Columns`:
   ```csharp
   private string GetLeafDescription(ColumnFilter leaf)
       => ClayFilterDescriptionBuilder.DescribeLeaf(
              leaf,
              sql => Columns.FirstOrDefault(c => c.SqlName == sql)?.DisplayName ?? sql);
   ```
   Собственную (одноклаузную) реализацию удалить.

## Критерии
- [ ] В форме настраиваемого фильтра колоночное условие с двумя клаузами показывается
      полностью («… содержит «прот» И содержит «моч»»), как в панели грида.
- [ ] Описание листа строится одним методом (`DescribeLeaf`) — без дубля логики.
- [ ] Поведение SQL не меняется (оба условия и так применялись).
- [ ] `dotnet build` без ошибок.

## Примечание
Редактирование такого условия остаётся через диалог колонки (там же задаются оба клауза);
в форме настраиваемого фильтра оно по-прежнему только отображается и удаляется (F9).
