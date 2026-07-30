> Часть серии **CF**. Прочитать `CF0_README_extract_filter.md` и результат **CF_C**.
> Делать ТОЛЬКО этот шаг. **Прямое указание на правку документации** (иначе `AGENTS.md`/`docs`
> не трогаются).

# CF_D — документация и тесты после переноса фильтра

Фильтр переехал в `Components/Filter/`, неймспейс сменился. Документация и имена неймспейсов
в тестах теперь указывают на несуществующее расположение — привести в соответствие. Правится
только перечисленное; прочую документацию не трогать.

## Прочитать

- отчёты **CF_A** и результат **CF_C** (что переехало, что осталось);
- фактические перенесённые файлы (неймспейс, состав).

## 1. Тесты

`tests/Clayzor.Lib.Web.Controls.Tests/` — файлы `ClayCompositeSqlBuilderTests.cs`,
`ClayFilterDescriptionBuilderTests.cs`, `ClayFilterJsonConverterTests.cs`,
`ClayFilterUrlHelperTests.cs` (и любые, где встречается `...Components.Grid.Filter`):
- заменить `using Clayzor.Lib.Web.Controls.Components.Grid.Filter;` на
  `using Clayzor.Lib.Web.Controls.Components.Filter;`;
- **ожидания (`Assert`) не трогать** — behavior-preserving;
- если тест конструировал `ClayColumnMeta` как вход диалога — перевести на `ClayFilterColumnInfo`
  (по CF_B); если тест не про фильтр — не касаться.

`README.md` тестового проекта — в таблице «Состав тестов» строки про фильтр: обновить
упоминание неймспейса, если оно есть; список файлов не менять.

## 2. `docs/clay-grid.md`

- разделы «Составной фильтр», «Сериализация и URL-персистенция», «ClayFilter*» — заменить
  неймспейс `Components/Grid/Filter/` → `Components/Filter/` в путях и упоминаниях;
- добавить одну фразу: фильтр вынесен в общий неймспейс `...Components.Filter` и используется
  и гридом, и деревом (серия CF); диалог принимает `ClayFilterColumnInfo`;
- `ClayColumnFilterDialog`/`ClayColumnValueFilterDialog` описать как **гридовые** (остались в
  `Components/Grid/`), в отличие от общего `ClayFilterDialog`.

## 3. `docs/clay-column-filter-dialog.md`

Проверить пути/неймспейсы к `ClayFilterValueEditor` и типам фильтра — они переехали. Сам диалог
остался в гриде — это указать.

## 4. `src/Clayzor.Lib.Web.Controls/AGENTS.md`

- в таблице компонентов у строк `ClayFilterDialog`, `ClayFilterGroup`, `ClayFilterExpression`,
  `ClayFilterValueEditor`, `ClayCompositeSqlBuilder`, `ClayFilterDescriptionBuilder`,
  `ClayFilterJsonConverter`, `ClayFilterUrlHelper`, `ClayFilterStrings` — обновить путь на
  `Components/Filter/`;
- добавить строку `ClayFilterColumnInfo` (описание фильтруемого поля — контракт диалога, не
  фильтр);
- **отметить разграничение:** `ClayFilterDialog` (общий, настраиваемый фильтр — дерево И/ИЛИ) vs
  `ClayColumnFilterDialog`/`ClayColumnValueFilterDialog` (гридовые, одна колонка);
- в разделе о зависимостях: фильтр не зависит от грида (можно использовать из дерева);
- строка про серию CF в истории шагов, если она ведётся.

## 5. `README.md` библиотеки

Если упоминается расположение фильтра или что он «часть грида» — поправить: общий компонент
`Components/Filter/`.

## Не делай

- Не правь документацию вне перечисленного.
- Не переписывай разделы «получше» — только фактические расхождения после переноса.
- Не выдумывай примеры: код в документации — скопирован из реальных файлов после CF_C.
- Не документируй фильтр дерева (серия TF).

## Проверка

- `grep -rn "Components.Grid.Filter\|Components/Grid/Filter" src/*/docs/ src/*/AGENTS.md src/*/README.md tests/`
  → пусто (кроме архива `promts/_done/`);
- каждый путь/неймспейс в правленой документации существует в коде после CF_C;
- `dotnet build` + `dotnet test` — зелёные;
- прочитать обновлённые разделы глазами: разграничение «общий `ClayFilterDialog` vs гридовые
  диалоги колонки» читается однозначно.

## Definition of Done серии CF

Проверить пункты DoD из `CF0` целиком. Серия CF закрыта — можно начинать **TF**.
