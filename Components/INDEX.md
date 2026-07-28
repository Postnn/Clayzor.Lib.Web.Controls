# Промты Clayzor — карта архива

Пути в репозитории:

- `Grid/_done/*` → `src/Clayzor.Lib.Web.Controls/Components/Grid/promts/_done/`
- `Grid/SH_share/*` → `src/Clayzor.Lib.Web.Controls/Components/Grid/promts/_done/SH_share/`
- `Tree/*` → `src/Clayzor.Lib.Web.Controls/Components/Tree/promts/`

Раскладку внутри `_done/` подгони под то, как файлы уже лежат в репозитории — здесь она
условная (`_done/CGO/`, `_done/CGR/`).

## Grid — выполнено

### CGO — сведение параметров тега `ClayGrid` в `ClayGridOptions`

| Файл | Шаг | Состояние |
|---|---|---|
| `CGO0_README_grid_options.md` | оркестратор: классификация параметров, решения, регрессионный чек-лист | ✅ |
| `CGO_A1_parameter_inventory.md` | инвентаризация параметров по коду (без кода) | ✅ |
| `CGO_A2_options_class.md` | класс `ClayGridOptions` | ✅ |
| `CGO_A3_internal_composition.md` | `_opt` внутри грида, перевод всех внутренних чтений | ✅ |
| `CGO_A4_tests.md` | тесты дефолтов и обнаружения конфликтов | ✅ |
| `CGO_B1_iclaygrid_and_pagebase.md` | `IClayGrid.Options` + `ClayGridPageBase` | ✅ |
| `CGO_B2_medicaltests_page.md` | статическая страница на `_gridOptions` | ✅ |
| `CGO_B3_inventory_home_page.md` | динамическая страница на `_gridOptions` | ✅ |
| `CGO_B4_remaining_consumers.md` | остатки потребителей по grep | ✅ |
| `CGO_C1_remove_legacy_parameters.md` | удаление legacy-`[Parameter]` | ✅ |
| `CGO_C2_documentation.md` | `AGENTS.md`, `docs/`, `README` | ✅ |
| `CGO_D1_options_convention.md` | конвенция Options для библиотеки | ✅ |

Итог: у `ClayGrid` 10 параметров тега вместо ~30; конфигурация — в `ClayGridOptions`
(21 свойство, `Defaults`); внутри компонента единственный источник настроек — поле `_opt`.

Открытые вопросы от D1 (решение за заказчиком): группировка `ClayGridOptions` по секциям при
росте свыше ~30 свойств; переименование неудачных имён `Id`, `Dynamic`, `SelectVisible`.

### CGR — переименования по конвенции

| Файл | Шаг | Состояние |
|---|---|---|
| `CGR1_rename_dynamic_settings.md` | `ClayGridDynamicOptions` → `ClayGridDynamicSettings` | ✅ |

Правило имён: `Clay*Options` — конфигурация экземпляра компонента на странице;
`Clay*Settings` — настройки уровня приложения (из конфигурации, через DI).

**Незакрытый хвост:** в `src/Clayzor.Lib.Web.Controls/AGENTS.md`, раздел «Настройки компонентов
(Options)», пункт 7 всё ещё пишет `Clay*DynamicOptions`. Правка — одно слово.

## Grid — к выполнению

### SH — «Поделиться» настройками динамического грида

| Файл | Этап | Состояние |
|---|---|---|
| `SH0_README_share.md` | индекс, правила, ловушки, решения, DoD | ⏳ **ревизия после CGO** |
| `SH1_discovery.md` | разведка по коду, без изменений | ⏳ **ревизия после CGO** |
| `SH2_schema.md` | схема стенда: таблица, поле, ключ, триггер, функция | ⏳ |
| `SH3_settings_and_dalc.md` | настройки и слой доступа к данным | ⏳ **ревизия после CGO** |
| `SH4_param_registry.md` | единый реестр имён параметров грида | ⏳ **ревизия после CGO** |
| `SH5_share_button.md` | кнопка «Поделиться» и диалог | ⏳ **ревизия после CGO** |
| `SH6_url_and_clipboard.md` | формирование URL и буфер обмена | ⏳ |
| `SH7_shared_list.md` | список общих настроек | ⏳ **исправлен блокер** |
| `SH8_shared_mode.md` | режим `sharedId`, запрет сохранения | ⏳ |
| `SH9_tests_manual_report.md` | тесты, ручной тест, отчёт | ⏳ **ревизия после CGO** |
| `SH_CHANGELOG_after_CGO.md` | что и почему изменено при ревизии | — справка |

Требует решения до старта: имя query-параметра `sharedId` — оставить константой или добавить
`SharedIdQueryParam` в `ClayGridDynamicSettings` (`SH0`, решение 8).

## Tree — к выполнению

| Файл | Шаг | Состояние |
|---|---|---|
| `CT1_tree_view_skeleton.md` | `ClayTreeView`: структура классов, ленивая загрузка уровней, тестовая страница | ✅ |

Редакция 2 учитывает итоги CGO и CGR1: вся конфигурация дерева — в `ClayTreeOptions`
(7 параметров тега), обязательный тест-защёлка на дефолты, эталоны разметки — `Home.razor`
и `MedicalTests.razor` после CGO. Отличия от первой редакции — в §11 файла.

Дорожная карта CT2–CT8 — в §10 файла CT1; отдельных промтов на них пока нет.
