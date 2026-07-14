# readme_grid_dynamic.md — оркестратор плана «Динамический режим ClayGrid»

Этот файл — точка входа и контроль порядка. План разбит на отдельные файлы (один файл = один
шаг) в папке `grid-dynamic/`. Агент выполняет ИМЕННО по одному файлу за заход.

# ClayGrid — динамический режим: подробные промты для агента

Динамический режим: грид берёт определение (SQL, колонки, кнопки) и сохраняет параметры
пользователя из/в базу данных. Промты дробные, по порядку, с точными путями, сигнатурами и
проверками. Один промт = один коммит.

## Как агент работает с КАЖДЫМ промтом (обязательно)
1. Делай РОВНО один промт за заход. Не забегай вперёд.
2. СНАЧАЛА открой и прочитай файлы из блока «Прочитать перед началом». Не пиши код, пока не
   понял существующие сигнатуры. НЕ выдумывай API — используй методы, которые реально есть.
3. Пиши минимум кода. Не рефактори соседнее. Не переименовывай существующее.
4. После кода: `dotnet build` зелёный + добавь/прогони тесты из соответствующего TG-промта.
5. Выполни блок «Проверка» буквально (входные данные → ожидаемый результат).
6. Если реальная сигнатура/поведение существующего кода расходится с промтом — ОСТАНОВИСЬ и
   спроси, не додумывай.

## Зафиксированные решения
- Настройки динрежима — объект **ClayGridDynamicOptions** (в коде) + байнд из appsettings.
- Имена таблиц/колонок — **конфигурируемый маппинг (ClayGridSchemaMap)**, всё читает из него.
- Типы колонок 1–13 — **поэтапно**: базовые (1,2,3,4,7) → сложные (5,6,9,11) → спецвывод (8,10,12,13).
- Условие фильтра в URL — **`КлючURL=op~value`**, `op` необязателен (нет → дефолт для типа).
- Нейминг **Clay*/Clayzor.*** (переименование уже применено).

## Общие правила
- **ДОСТУП К БД — СТРОГО ЧЕРЕЗ `Clayzor.Lib.Entities`.** Весь SQL выполняется помощниками
  `Entity.*` (`GetPagedAsync`/`GetCountAsync`/`GetAllAsync`, `InsertAsync`/`UpdateAsync`/
  `DeleteAsync`) и классами данных, живущими в `Clayzor.Lib.Entities`. Слой Controls
  (`ClayGrid` и код динрежима) **НИКОГДА не создаёт `DbManager` и не выполняет SQL сам** —
  он вызывает классы из `Lib.Entities`, передавая инжектированный `DbManager Db`
  (`[Inject] protected DbManager Db` уже есть в `ClayGridPageBase`). Для динамических
  (произвольных) запросов добавляются помощники в `Lib.Entities` — см. шаг **G1b**.
- `DbManager` регистрируется в DI приложения с уже резолвнутой строкой подключения. Если
  динрежиму нужна ДРУГАЯ строка (по `ConnectionStringName` из опций) — резолвинг и создание
  `DbManager` тоже инкапсулируются в `Lib.Entities` (фабрика/провайдер там), а не в Controls.
- SQL — SQL Server 2008 R2-совместимый. Все параметры — через `@param`, без конкатенации значений.
- Расширяем существующее, не переписываем: `ClayDataQuery`, партиалы `ClayGrid.*`
  (Filtering/Grouping/Sorting/Paging/Selection), `ColumnTypeRegistry` + дескрипторы,
  композитный фильтр (`ClayFilterJsonConverter`, `ClayCompositeSqlBuilder`, `ClayFilterGroupNode`,
  `ColumnFilter`, `ColumnFilterOperator`), `ClayGridPageBase<T>`.
- Стиль/разметка — по STYLE_RULES.md (никакого инлайн-визуала).
- Код динрежима: доступ к данным — в `src/Clayzor.Lib.Entities/` (папка `DynamicGrid/`);
  UI/склейка грида — в `src/Clayzor.Lib.Web.Controls/Components/Grid/Dynamic/`.

---

---

## Порядок выполнения (реализация)

Выполнять строго сверху вниз. После каждого шага Gx — прогнать соответствующий TGx (см.
карту ниже), зелёная сборка и ручная «Проверка» из файла шага.

| Шаг | Файл | Что делает |
|---|---|---|
| G0 | `grid-dynamic/G00_sql_schema_seed.md` | SQL: 3 таблицы + триггер-upsert + сид грида #140 (dev/тест-стенд) |
| G1 | `grid-dynamic/G01_options_schema_map.md` | ClayGridDynamicOptions + ClayGridSchemaMap + DI/appsettings |
| G1b | `grid-dynamic/G01b_entities_dynamic_helpers.md` | Помощники динамического SQL в Lib.Entities (весь доступ к БД — здесь) |
| G2 | `grid-dynamic/G02_definition_repository.md` | Классы данных определения/колонок в Lib.Entities + мапперы |
| G3 | `grid-dynamic/G03_type_map.md` | Тип(int)→дескриптор + дефолтный оператор + разбор Формат (1,2,3,4,7) |
| G4 | `grid-dynamic/G04_dynamic_render.md` | Динамический режим: собрать грид из определения (read-only) |
| G5 | `grid-dynamic/G05_row_action_buttons.md` | Кнопки из определения: редактирование/добавление/удаление |
| G6 | `grid-dynamic/G06_user_params_repository.md` | Параметры пользователя в Lib.Entities (INSERT-only) |
| G7 | `grid-dynamic/G07_state_persistence.md` | Сохранение/восстановление состояния на каждой загрузке |
| G8 | `grid-dynamic/G08_url_filter_parser.md` | Разбор URL-фильтра op~value + правила 1,2,5 |
| G9 | `grid-dynamic/G09_url_columns_forced.md` | Видимость колонок из URL + forced не сохраняется |
| G10 | `grid-dynamic/G10_type_list.md` | Тип 5 (Список): подзапрос один раз, мультивыбор-фильтр |
| G11 | `grid-dynamic/G11_type_icon.md` | Тип 9 (Пиктограмма): 3-колоночный подзапрос |
| G12 | `grid-dynamic/G12_type_conditions.md` | Тип 6/11 (Условие булево/список): фильтр-онли |
| G13 | `grid-dynamic/G13_type_html_limited.md` | Тип 8/12/4: HTML+санитизация, обрезка строки |
| G14 | `grid-dynamic/G14_type_datetime_local.md` | Тип 10/13: локализованные дата/время (UTC→client) |

## Тесты

Стек: xUnit + Dapper, .NET 10, проект `Clayzor.Lib.Web.Controls.Tests`. Стиль как в
существующих тестах (русские XML-doc summary, `[Fact]`/`[Theory]`), БЕЗ мок-фреймворка
(его в проекте нет) — чистые функции или ручные фейки-заглушки.

Правило тестируемости БД: классы данных G2/G6 (в `Lib.Entities`) разбиты на «выполнить SQL» и
«построить SQL / смапить строки». Юнит-тесты вызывают чистые функции (`BuildGridSql`,
`BuildColumnsSql`, `MapDefinition`, `MapColumn`, `BuildParamName`, `BuildInsertSql`) на
рукотворных данных. Реальный SQL — только в TG-INT на LocalDB (сид из G0), помечены
`[Trait("Category","Integration")]`, чтобы CI без БД их пропускал.

Каждый TG-промт: создать файл `<Имя>Tests.cs`, покрыть перечисленные кейсы, `dotnet test` зелёный.

| Тест | Файл | К шагу |
|---|---|---|
| TG1 | `grid-dynamic/TG1_options_tests.md` | Тесты опций/схемы (к G1) |
| TG2 | `grid-dynamic/TG2_definition_mapper_tests.md` | Тесты мапперов определения/колонок (к G2) |
| TG3 | `grid-dynamic/TG3_type_map_tests.md` | Тесты типов/оператора/Формат (к G3) |
| TG4 | `grid-dynamic/TG4_url_filter_parser_tests.md` | Тесты разбора URL-фильтра — ключевой (к G8) |
| TG5 | `grid-dynamic/TG5_user_params_tests.md` | Тесты параметров пользователя (к G6/G7) |
| TG6 | `grid-dynamic/TG6_state_serialization_tests.md` | Тесты round-trip состояния (к G7) |
| TG7 | `grid-dynamic/TG7_complex_types_tests.md` | Тесты сложных типов 5/6/9/11 (к G10–G12) |
| TG8 | `grid-dynamic/TG8_special_types_tests.md` | Тесты спецвывода 8/12/10/13 (к G13–G14) |
| TG-INT | `grid-dynamic/TG9_integration_tests.md` | Интеграционные на LocalDB (опц., к G0/G2/G6) |

## Карта «шаг → тест» (когда что запускать)

- G1 → TG1 · G1b → TG-INT (смоук) · G2 → TG2 · G3 → TG3 · G8 → TG4 · G6/G7 → TG5,TG6 ·
  G10–G12 → TG7 · G13–G14 → TG8 · G0/G2/G6 → TG-INT (интеграционные, на LocalDB).

## Definition of Done (по каждому шагу)
- [ ] Прочитаны файлы из блока «Прочитать перед началом».
- [ ] Реализован ТОЛЬКО текущий шаг, соседнее не тронуто.
- [ ] `dotnet build` зелёный.
- [ ] Добавлен/зелёный соответствующий TG-тест.
- [ ] Выполнен блок «Проверка» (вход→ожидаемый результат совпал).
- [ ] Один шаг = один коммит.

---

## Порядок и проверяемость
Выполнять строго: G0 → G1 → G1b → G2 → … → G14, после каждого — зелёная сборка + соответствующий TG-тест +
ручная «Проверка». Фазы 0–3 дают рабочий динамический грид с персистенцией и URL-фильтрацией;
фазы 4–5 добавляют оставшиеся типы колонок. При любой неоднозначности в конкретном Тип/Формат —
остановиться и спросить.

---

# Тесты (xUnit) — по одному промту на группу

Стек: xUnit + Dapper, .NET 10, проект `Clayzor.Lib.Web.Controls.Tests`. Стиль как в
существующих тестах (русские XML-doc summary, `[Fact]`/`[Theory]`), БЕЗ мок-фреймворка
(его в проекте нет) — чистые функции или ручные фейки-заглушки.

Правило тестируемости БД: классы данных G2/G6 (в `Lib.Entities`) разбиты на «выполнить SQL» и
«построить SQL / смапить строки». Юнит-тесты вызывают чистые функции (`BuildGridSql`,
`BuildColumnsSql`, `MapDefinition`, `MapColumn`, `BuildParamName`, `BuildInsertSql`) на
рукотворных данных. Реальный SQL — только в TG-INT на LocalDB (сид из G0), помечены
`[Trait("Category","Integration")]`, чтобы CI без БД их пропускал.

Каждый TG-промт: создать файл `<Имя>Tests.cs`, покрыть перечисленные кейсы, `dotnet test` зелёный.

## TG1 — ClayGridDynamicOptions / ClayGridSchemaMap (к G1)  → OptionsBindingTests.cs
```
- байнд из in-memory IConfiguration (Dictionary<string,string?>) с ключами вида
  "ClayGrid:Dynamic:ConnectionStringName"="Main", "...:SettingsTable"="T2" — все поля заполнены;
- дефолты схемы: new ClayGridSchemaMap().Settings.Title == "Запрос",
  .Columns.Type == "Тип", .UserParams.Name == "Параметр";
- Validate() при пустом ConnectionStringName бросает InvalidOperationException, текст содержит
  "ConnectionStringName".
```

## TG2 — мапперы определения/колонок (к G2)  → DefinitionMapperTests.cs
```
- MapDefinition на словаре строки (ключи = имена из схемы) → все поля ClayGridDefinition верные;
- MapColumn на строке с Порядок=0 → ClayColumnDefinition.Order==0 (не отброшено), Type==7;
- MapColumn с изменённым ClayGridSchemaMap (напр. Header:"Caption") читает значение из колонки
  "Caption" — подтверждает, что имена берутся из схемы, а не захардкожены.
```

## TG3 — типы/оператор/Формат (к G3)  → ColumnTypeMapTests.cs
```
[Theory] по Тип 1,2,3,4,7:
- ClayColumnTypeMap.Resolve(тип) не null и соответствует ожидаемому дескриптору;
- Resolve(5),Resolve(6),Resolve(8..13) == null; IsSupported(5)==false;
- ClayDefaultOperator.For: Text→Contains, Number→Equals, Date→Equals, Bool→Equals;
- ClayColumnFormat.Parse(1,"N2")→"N2"; Parse(3,"dd.MM.yyyy")→"dd.MM.yyyy"; Parse(7,"Активно=1")→"Активно=1".
```

## TG4 — разбор URL-фильтра, правила 1,2,5 (к G8) — КЛЮЧЕВОЙ  → UrlFilterParserTests.cs
```
[Theory] ClayGridUrlFilterParser.Parse:
- ("name","eq~DQA1",col:Text) → Operator=Equals, Value="DQA1", IsForced=true, IsDefault=false;
- ("created","ge~20260101",col:Date) → GreaterOrEqual,"20260101";
- ("type","in~3,5",col:Number) → In,"3,5";
- ("created","between~20260101~20260401",Date) → Between с обеими границами;
- ("created","20260101",Date) без "op~" → Operator = дефолт Date (Equals), Value="20260101" (правило 5);
- ("_name","eq~x",Text) → IsDefault=true (ключ 'name');
[Fact] Apply-логика:
- _key при отсутствии savedUserParams[key] → условие добавлено; при наличии → НЕ добавлено (сохранённое победило);
- key без '_' → добавлено и помечено forced (не для сохранения).
```

## TG5 — параметры пользователя: имена/CLID (к G6/G7)  → UserParamsTests.cs
```
- BuildParamName("flt",140)=="flt140"; ("cols",1)=="cols1";
- CLID: если query-параметра CLID нет → используется 0 (проверить на хелпере извлечения CLID);
- фейк IClayGridUserParamsRepository фиксирует, что SaveAsync вызывает путь INSERT (в C# нет
  отдельного UPDATE) — проверить, что реализация не содержит UPDATE/MERGE (или через шов-счётчик).
```

## TG6 — round-trip состояния (к G7)  → GridStateSerializationTests.cs
```
- сериализация→десериализация: видимость+порядок колонок (п.6), сортировка (п.9), группировка (п.8),
  размер страницы (п.10) — равны исходным по всем полям;
- фильтр (п.7): построить ClayFilterGroupNode с 2 ColumnFilter → JSON (ClayFilterJsonConverter) →
  обратно → эквивалентно исходному.
```

## TG7 — сложные типы 5/6/9/11 (к G10–G12)  → ComplexColumnTypesTests.cs
```
- фейковый источник справочника со счётчиком: рендер N строк колонки Тип 5/9 → счётчик выполнений
  подзапроса == 1;
- Тип 6/11: колонка не входит в набор выводимых колонок и в набор группируемых, но входит в
  набор фильтруемых;
- Тип 11: выбранный whereExpr появляется в SQL, собранном ClayCompositeSqlBuilder (assert по подстроке
  выражения в WHERE).
```

## TG8 — спецвывод 8/12/10/13 (к G13–G14)  → SpecialColumnTypesTests.cs
```
- ClayHtmlSanitizer.Sanitize("<b>ok</b><script>alert(1)</script>") → не содержит "<script"/"alert",
  "<b>ok</b>" сохранён; onclick/"javascript:" вырезаны;
- Тип 12: обрезка "abcdefgh" по длине 5 → "abcde…", полный текст доступен как tooltip;
- Тип 10/13: чистая функция конвертации с ЯВНЫМ смещением: UTC 2026-01-01T09:00:00Z, смещение +03:00,
  формат "HH:mm" → "12:00".
```

## TG-INT — интеграционные на LocalDB (опционально, к G0/G2/G6)  → DynamicGridIntegrationTests.cs
```
Все методы: [Trait("Category","Integration")]. Предусловие — LocalDB со стендом из G0 (grid #140).
- LoadGridAsync(140) → Title=="Медицинские исследования", IdColumn=="КодИсследования";
- LoadColumnsAsync(140) → 5 колонок в порядке Порядок (1005 последняя);
- SaveAsync(0,"flt140","a") + SaveAsync(0,"flt140","b") → LoadAsync → flt140=="b" (одна строка, триггер);
- в CI без БД тесты пропускаются фильтром по Trait (`dotnet test --filter Category!=Integration`).
```
