> Часть серии **CF** (извлечение `ClayFilter`). Прочитать `CF0_README_extract_filter.md`.
> Делать ТОЛЬКО этот шаг. **Кода нет** — результат в ответе.

# CF_A — инвентаризация: что переносим и что за это цепляется

Перед переносом файлов и сменой контракта нужна карта: какие файлы относятся к фильтру, кто на
них ссылается снаружи, и что делать с пограничными случаями (`ColumnTypes`, `ClayFilterValueEditor`).
Ошибка здесь — пропущенная ссылка, из-за которой CF_C не соберётся в неожиданном месте.

## Прочитать

Всё из раздела «Прочитать» оркестратора.

## Что сделать

**1. Список файлов фильтра.** Механически:

```
ls Components/Grid/Filter/
grep -rln "ClayFilter\|ColumnFilter\|ValueFilter\|CompositeFilter" src/Clayzor.Lib.Web.Controls/Components/Grid/
```

Раздели найденное на три группы:
- **MOVE** — переносится в `Components/Filter/` (ядро фильтра);
- **STAY** — остаётся в гриде (`ClayColumnMeta`, `ClayGridUrlFilterParser`,
  `ClayGridUserParamsData`, всё про динамический режим грида);
- **BORDER** — пограничное, решение обосновать: `ColumnTypes/*`, `ClayFilterValueEditor`.
  (`ClayColumnFilterDialog`, `ClayColumnValueFilterDialog`, `OpenConditionRequest` — уже решено:
  **STAY в гриде**, см. CF0; но проверить, что после переноса ядра они ссылаются на новый
  неймспейс и собираются.)

**2. Карта внешних ссылок.** Для каждого MOVE-типа — кто на него ссылается вне папки `Filter/`:

```
grep -rn "\bClayFilterDialog\b" src/ tests/
```

(и так по каждому). Особое внимание — `ClayGrid.Filtering.cs`, `ClayGrid.razor`,
`ClayGridPageBase*`, `ClayGrid.Dynamic.cs`, диалоги, тесты. Это карта работ для CF_C:
каждая внешняя ссылка после переноса потребует правки `using`.

**3. Судьба `ColumnTypes/`.** Ответить по коду, а не по наитию:
- ссылается ли `ColumnTypes` на что-либо из грида (`ClayColumnMeta`, `ClayGrid`, динамику)?
  `grep -rn "ClayGrid\|ClayColumnMeta" src/Clayzor.Lib.Web.Controls/Components/Grid/ColumnTypes/`;
- кто, кроме фильтра, использует `ColumnTypes` (сам грид — рендер ячеек, сортировка)?
- вывод: **перенести** в `Components/ColumnTypes/` (если нейтрален и нужен и фильтру, и гриду),
  **оставить** со ссылкой из фильтра (если сильно связан с гридом), или **разделить**. Дать
  рекомендацию с обоснованием — решает заказчик, но выбор подготовить.

**4. Состав `ClayFilterColumnInfo` (описание фильтруемого поля, НЕ фильтр) vs `ClayColumnMeta`.** Выписать **все** поля `ClayColumnMeta`,
которые реально читаются внутри `Components/Grid/Filter/` и в диалогах
(`ClayColumnFilterDialog`, `ClayColumnValueFilterDialog`). Это определит минимальный состав
`ClayFilterColumnInfo` в CF_B. Проверить гипотезу оркестратора: нужны только `SqlName`,
`DisplayName`, `Type` (дескриптор), `Options`/lookup, `BoolTrueLabel`/`BoolFalseLabel`. Если
всплывёт что-то ещё (`AllowValueFilter`?) — зафиксировать.

**5. Обратные ссылки фильтра на грид.** Цель серии — их обнулить. Сейчас:

```
grep -rn "ClayColumnMeta\|\bClayGrid\b\|Grid\." src/Clayzor.Lib.Web.Controls/Components/Grid/Filter/
```

Каждое попадание — это то, что CF_B/CF_C обязаны развязать. Выписать пофайлово.

**6. Тесты.** Список `*Tests.cs`, ссылающихся на переносимые типы (по неймспейсу
`...Grid.Filter`). Это правки CF_D.

## Формат отчёта

- таблица файлов: путь → MOVE/STAY/BORDER → чем обоснован BORDER;
- карта внешних ссылок: тип → файлы-потребители (числа);
- состав `ClayFilterColumnInfo` (поля, каждое — зачем, кто читает);
- рекомендация по `ColumnTypes` с обоснованием;
- список обратных ссылок фильтра на грид (что развязывать);
- список тестов к правке неймспейсов.

## Не делай

- Не переноси ни одного файла, не меняй неймспейсы, не создавай `ClayFilterColumnInfo` — это CF_B/CF_C.
- Не решай судьбу `ColumnTypes` единолично, если она неочевидна, — готовь рекомендацию + вопрос.
- Не трогай документацию.
- `git status` после шага — чистый.

## Проверка

- в отчёте každый файл из `Components/Grid/Filter/` классифицирован;
- для каждого MOVE-типа указан хотя бы один потребитель (ноль → мёртвый тип, отдельная строка);
- гипотеза состава `ClayFilterColumnInfo` подтверждена или скорректирована списком реально
  читаемых полей;
- дан однозначный ответ (или обоснованная развилка) по `ColumnTypes` и по каждому BORDER-файлу;
- ни один файл в репозитории не изменён.
