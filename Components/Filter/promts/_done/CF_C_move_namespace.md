> Часть серии **CF**. Прочитать `CF0_README_extract_filter.md`, отчёты **CF_A** и результат
> **CF_B**. Делать ТОЛЬКО этот шаг. **Высокий риск** — перед началом git-коммит как точка отката.

# CF_C — перенос фильтра в `Components/Filter/` и смена неймспейса

Механический шаг: физически переместить ядро фильтра из `Components/Grid/Filter/` в
`Components/Filter/`, сменить неймспейс `...Components.Grid.Filter` → `...Components.Filter`,
починить все ссылки. Контракт уже развязан (CF_B), поэтому здесь — только перемещение и `using`.

## Прочитать

- отчёт **CF_A**: список MOVE-файлов и карта внешних ссылок (это план работ);
- решение CF0 по `ColumnTypes` (перенести/оставить — по рекомендации CF_A, утверждённой заказчиком).

## 1. Перенести файлы (MOVE-список из CF_A)

`git mv` (не «создать+удалить» — сохраняем историю) каждого файла ядра фильтра из
`Components/Grid/Filter/` в `Components/Filter/`:
`IClayFilterNode`, `ClayFilterGroupNode`, `ColumnFilter`, `ValueFilter`, `ClayFilterSource`,
`ClayFilterColumnInfo` (из CF_B), `ClayFilterExpression.*`, `ClayFilterGroup.*`,
`ClayFilterDialog.*`, `ClayFilterValueEditor.*`, `ClayCompositeSqlBuilder`,
`ClayFilterDescriptionBuilder` (+ `FilterSegment`), `ClayFilterJsonConverter`,
`ClayFilterUrlHelper`, `ClayFilterStrings`, `ClayFilterOperatorLabels`, `ClayFilterOption`.

`ColumnTypes/` — переносить в `Components/ColumnTypes/` **только если** так решено по CF_A;
иначе оставить на месте и в перенесённых файлах сослаться на текущий неймспейс.

**НЕ переносить** (STAY, решение CF0): `ClayColumnFilterDialog.*`,
`ClayColumnValueFilterDialog.*`, `OpenConditionRequest`, `ClayColumnMeta`,
`ClayGridUrlFilterParser`, `ClayGridUserParamsData`.

## 2. Сменить неймспейс

Во **всех** перенесённых файлах: `namespace Clayzor.Lib.Web.Controls.Components.Grid.Filter;`
→ `namespace Clayzor.Lib.Web.Controls.Components.Filter;`. В `.razor`-файлах фильтра —
директивы `@namespace`/`@using`, если есть.

## 3. Починить ссылки (по карте CF_A)

Каждый внешний потребитель (грид, диалоги колонки, `ClayGridPageBase`, `ClayGrid.Dynamic.cs`,
тесты) получает `using Clayzor.Lib.Web.Controls.Components.Filter;` вместо
`...Components.Grid.Filter`. Пройти по карте из CF_A файл за файлом.

`_Imports.razor` библиотеки: если там был `@using ...Components.Grid.Filter` — заменить/добавить
`@using Clayzor.Lib.Web.Controls.Components.Filter`.

Диалоги, оставшиеся в гриде (`ClayColumnFilterDialog` и т.д.), теперь ссылаются на новый
неймспейс для `ClayFilterValueEditor`, `ColumnFilter`, `ValueFilter`, `ClayFilterOption` —
добавить им `using`.

## 4. Проверить обратную развязку

Цель серии — фильтр не зависит от грида. После переноса:

```
grep -rn "ClayGrid\|ClayColumnMeta\|Components.Grid" src/Clayzor.Lib.Web.Controls/Components/Filter/
```

→ **пусто.** Любое попадание — незакрытая зависимость: развязать (обычно это `ClayColumnMeta`,
который должен был уйти в `ClayFilterColumnInfo` ещё в CF_B; если всплыл здесь — доделать).

## Ловушки

- **`.razor` + `.razor.cs` — один неймспейс.** Смена только в `.cs` при partial-компоненте
  оставит `.razor` в старом → ошибка компиляции или, хуже, дубль типа. Менять в обоих.
- **`@using` в разметке грида.** `ClayGrid.razor` мог открывать фильтр-компоненты по короткому
  имени через `@using` в `_Imports`. После смены неймспейса короткие имена перестанут
  резолвиться — проверить сборкой, а не только grep'ом.
- **Тесты.** Их неймспейсы правит CF_D, но если тест перестал **компилироваться** — это здесь
  (ссылка на перенесённый тип), почини `using`, но ожидания не трогай.
- **DI-регистрация.** Если `ClayFilterJsonConverter` регистрируется где-то в `Program.cs`/
  сериализаторе по полному имени типа — обновить.

## Не делай

- Не меняй логику, разметку, SQL — только расположение файлов и неймспейсы/using.
- Не переименовывай типы (не в этой серии).
- Не переноси STAY-файлы.
- Не «причёсывай» перенесённый код — diff должен быть перемещением + неймспейсом.

## Проверка

- `dotnet build Clayzor.sln` — зелёный. Падение = незакрытая ссылка из карты CF_A: чинить
  ссылку, не откатывать перенос;
- `dotnet test` — зелёный;
- `git status`: перенесённые файлы показаны как `R` (renamed), не delete+add;
- `grep -rn "Components.Grid.Filter" src/ tests/` → пусто (кроме архива `promts/_done/`);
- `grep -rn "ClayGrid\|ClayColumnMeta" src/Clayzor.Lib.Web.Controls/Components/Filter/` → пусто;
- полный регрессионный чек-лист CF0 (оба стенда) — поведение фильтра идентично.
