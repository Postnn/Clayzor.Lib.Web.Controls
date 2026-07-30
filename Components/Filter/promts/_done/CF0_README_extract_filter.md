# CF — извлечение `ClayFilter` из `ClayGrid` в общий компонент (оркестратор)

> Серия 1 из 2 большой задачи «фильтр для дерева». Эта серия **только извлекает** общий
> фильтр из грида; фильтрация дерева — отдельная серия `TF` **после** этой.
>
> Правила исполнения — корневой `/AGENTS.md`
> (**Think Before Coding → Simplicity First → Surgical Changes → Goal-Driven Execution**),
> плюс конвенция `_readme_grid_dynamic.md` (как агент работает с каждым промтом).
> Пользовательский текст — на русском. Каждый новый public/protected член — с `/// <summary>`.
> Git — только по прямому указанию. `AGENTS.md`/`docs/`/`README` правит только шаг **CF_D**.
>
> **Один файл = один заход = один коммит. Не забегать вперёд.** Класть в
> `src/Clayzor.Lib.Web.Controls/Components/Filter/promts/`.

---

## Зачем

Диалог настраиваемого фильтра нужен и гриду, и дереву (серия `TF`). Сейчас весь фильтр живёт
в `Components/Grid/Filter/` и `Components/Grid/`, в неймспейсе `...Components.Grid.Filter`, и
формально принадлежит гриду. Чтобы дерево не зависело от грида (`Controls` не должен требовать
`ClayGrid` ради фильтра), фильтр выносится в самостоятельный неймспейс
`Clayzor.Lib.Web.Controls.Components.Filter`.

**Цель серии: фильтр стал автономным компонентом, грид пользуется им как клиент, поведение
грида не изменилось ни на йоту.** Это чистый рефакторинг — как CGO, только про фильтр.

---

## Ключевое решение (подтверждено заказчиком)

**Общий контракт колонки — новый лёгкий тип `ClayFilterColumnInfo`**, а не гридовый
`ClayColumnMeta`. `ClayColumnMeta` тащит гридовые поля (`ColumnId`, `Groupable`,
`AllowValueFilter`, `SortName`), дереву не нужные. Диалог фильтра переводится на
`ClayFilterColumnInfo` (`SqlName`, `DisplayName`, `Type`, `Options`) — это **описание фильтруемого
поля, не фильтр**; грид маппит свои `ClayColumnMeta` → `ClayFilterColumnInfo` в одном месте,
дерево (в `TF`) собирает `ClayFilterColumnInfo` из колонок своего SQL-запроса.

Обоснование: тип-переходник разрывает зависимость фильтра от грида раз и навсегда, ценой одного
маппера на стороне грида. Альтернатива «оставить `ClayColumnMeta` общим» дешевле сейчас, но
протаскивает гридовые поля в дерево и в фильтр — их придётся годами игнорировать и объяснять.

---

## Что переносится (инвентарь по коду)

**Уже в `Components/Grid/Filter/`** (неймспейс `...Grid.Filter`):
`IClayFilterNode`, `ClayFilterGroupNode`, `ColumnFilter`, `ValueFilter`, `ClayFilterSource`,
`ClayFilterExpression.razor(.cs)`, `ClayFilterGroup.razor(.cs)`, `ClayFilterDialog.razor(.cs)`,
`ClayFilterValueEditor.*`, `ClayCompositeSqlBuilder`, `ClayFilterDescriptionBuilder`
(+ `FilterSegment`), `ClayFilterJsonConverter`, `ClayFilterUrlHelper`, `ClayFilterStrings`,
`ClayFilterOperatorLabels`, `ClayFilterOption`.

**В `Components/Grid/` (не в папке Filter):** `ClayColumnFilterDialog.*` (диалог одной
колонки, ≤2 условия), `ClayColumnValueFilterDialog.*` (Excel-style), `OpenConditionRequest`.
**Решение (согласовано): остаются в гриде (STAY).** Дереву нужен только `ClayFilterDialog`
(настраиваемый фильтр — дерево И/ИЛИ); диалоги одной колонки — гридовая специфика (перетаскивание
заголовка), тащить их в общий неймспейс значит расширять «общее» тем, что общим не является.
Они продолжат ссылаться на общий `ClayFilterValueEditor`/типы из нового неймспейса — это нормально.

**Дескрипторы типов** — `Components/Grid/ColumnTypes/` (`ColumnTypeDescriptor`,
`ColumnTypeRegistry`, `ColumnType`, конкретные типы). Фильтр от них зависит напрямую. Их
перенос — **отдельный вопрос этапа CF_A** (возможно, они уже достаточно нейтральны, чтобы
жить в `Components/ColumnTypes/`; возможно, дешевле оставить и сослаться). **Не решать
заранее** — инвентаризация в CF_A даст ответ.

**НЕ переносится:** `ClayColumnMeta` (остаётся гридовым; фильтр от него отвязывается через
`ClayFilterColumnInfo`), `ClayGridUrlFilterParser` (разбор дерева `id~op~value` — гридовая тема
динамического режима; дерево получит свой разбор в `TF`), `ClayGridUserParamsData`
(персистенция — уровень грида/дерева, не фильтра).

---

## Зафиксированные решения (не пересматривать)

1. **Behavior-preserving.** Ни одного изменения поведения грида на всех шагах. Никаких новых
   фич, переименований членов (кроме неймспейса), правок SQL-генерации по существу.
2. **Неймспейс — новый, имена типов — прежние.** `ClayFilterDialog` остаётся
   `ClayFilterDialog`, меняется только `Grid.Filter` → `Filter`. Переименование самих типов —
   не в этой серии.
3. **`ClayFilterColumnInfo` — новый лёгкий тип**, единственный контракт колонки для фильтра
   (решение заказчика выше). Диалоги принимают `IReadOnlyList<ClayFilterColumnInfo>` вместо
   `IReadOnlyList<ClayColumnMeta>`.
4. **Грид остаётся клиентом фильтра.** Внутри грида добавляется один приватный маппер
   `ClayColumnMeta` → `ClayFilterColumnInfo`; всё остальное в гриде читает результат.
5. **Строки — в `ClayFilterStrings`**, он переезжает вместе с фильтром. Хардкода русских
   строк в новых местах не вводить.
6. **Тесты фильтра переезжают вместе с кодом.** `ClayCompositeSqlBuilderTests`,
   `ClayFilterDescriptionBuilderTests`, `ClayFilterJsonConverterTests`,
   `ClayFilterUrlHelperTests` — обновить неймспейсы, ожидания не трогать.
7. **`ClayFilterValueEditor`, `ColumnTypeDescriptor`-иерархия** — их судьба (перенести или
   оставить со ссылкой) определяется инвентаризацией CF_A, не догадками.

---

## Этапы и файлы

| # | Файл | Что делает | Зависит от | Риск |
|---|---|---|---|---|
| CF_A | `CF_A_inventory.md` | инвентаризация: что переносится, все внешние ссылки, судьба ColumnTypes; **кода нет** | — | нет |
| CF_B | `CF_B_filter_column_type.md` | новый тип `ClayFilterColumnInfo` + перевод диалогов на него (внутри грида — маппер) | CF_A | средний |
| CF_C | `CF_C_move_namespace.md` | физический перенос папки в `Components/Filter/`, смена неймспейса, глобальный fix ссылок | CF_B | **высокий** |
| CF_D | `CF_D_docs.md` | `AGENTS.md`, `docs/`, тесты-неймспейсы | CF_C | нет |

**Порядок обязателен.** CF_B развязывает контракт **до** переноса — тогда перенос (CF_C) чисто
механический (сменить неймспейс, поправить `using`). Обратный порядок смешал бы смену контракта
с переносом файлов в один необозримый диф. Перед CF_C — git-коммит как точка отката.

---

## Прочитать перед началом (один раз на серию)

- `/AGENTS.md`, `src/Clayzor.Lib.Web.Controls/AGENTS.md` (раздел про фильтр — большой);
- `Components/Grid/Filter/` — все файлы;
- `Components/Grid/ClayColumnFilterDialog.*`, `ClayColumnValueFilterDialog.*`;
- `Components/Grid/ColumnTypes/` — все файлы;
- `Components/Grid/ClayGrid.Filtering.cs` — как грид вызывает диалоги и строит описания;
- `Components/Grid/IClayGrid.cs` — `ClayColumnMeta`;
- `tests/Clayzor.Lib.Web.Controls.Tests/` — файлы `ClayFilter*Tests.cs`,
  `ClayCompositeSqlBuilderTests.cs`.

---

## Общий регрессионный чек-лист (после каждого шага с CF_B)

Рефакторинг behavior-preserving — любое расхождение есть дефект шага.

**Сборка**
- `dotnet build Clayzor.sln` — зелёный, без новых warning'ов;
- `dotnet test` — зелёный.

**Статический грид — `/medical-tests`**
- перетаскивание заголовка в трей фильтрации → диалог колонки → чип; редактирование чипа;
- настраиваемый (составной) фильтр: И/ИЛИ, вложенные группы, add/remove условий; текст фильтра
  в диалоге обновляется; один скролл;
- фильтр по значению (Excel-style): чекбоксы, tri-state, порог 100, взаимоисключение с условием;
- перетаскивание колонки при активном составном фильтре → условие через И, отмена не меняет фильтр;
- бейдж с числом условий на кнопке фильтра;
- текст/сегменты фильтра в трее, клик по сегменту открывает нужный диалог;
- очистка фильтра — единой кнопкой.

**Динамический грид — `/?id=140`**
- восстановление фильтра из URL (`?...&filter=...` или `id~op~value`);
- сохранение/восстановление фильтра в пользовательских параметрах по CLID;
- фильтр-онли колонки (типы 6/11) участвуют в фильтрации.

**Общее** — тёмная тема; печать (текст фильтра в шапке); в консоли браузера нет новых ошибок.

---

## Definition of Done серии

- [ ] `Components/Filter/` существует, неймспейс `...Components.Filter`, фильтр там целиком;
- [ ] диалоги принимают `ClayFilterColumnInfo`, не `ClayColumnMeta`;
- [ ] `grep -rn "Components.Grid.Filter" src/` → пусто (кроме архива `promts/_done/`);
- [ ] грид собран как клиент нового неймспейса, поведение по чек-листу идентично;
- [ ] `Clayzor.Lib.Web.Controls` не приобрёл новых зависимостей; дерево (в `TF`) сможет
      использовать фильтр, не ссылаясь на `ClayGrid` — проверяется в `TF`, здесь только
      отсутствие обратных ссылок фильтра на грид (`grep -rn "ClayGrid\|ClayColumnMeta"
      src/Clayzor.Lib.Web.Controls/Components/Filter/` → пусто).

## Вне рамок серии

Фильтрация дерева, обход/подсветка/«отфильтровано», значения по умолчанию, сохранение
состояния дерева, query-параметры дерева — всё это серия **TF**. Переименование типов фильтра,
разбиение `ClayColumnMeta` — не здесь.
