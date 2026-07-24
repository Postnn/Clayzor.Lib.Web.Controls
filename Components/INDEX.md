# Промты Clayzor — карта архива

Собрано на текущий момент. Пути в репозитории:

- `Grid/*` → `src/Clayzor.Lib.Web.Controls/Components/Grid/promts/`
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
| `CGO_D1_options_convention.md` | конвенция Options для библиотеки (опциональный шаг) | ✅ |

Итог серии: у `ClayGrid` 10 параметров тега вместо ~30; конфигурация — в `ClayGridOptions`;
правило зафиксировано в `AGENTS.md`, раздел «Настройки компонентов (Options)».

Открытые вопросы, оставленные шагом D1 (решение за заказчиком):
- группировка `ClayGridOptions` по секциям при росте свыше ~30 свойств;
- переименование неудачных имён: `Id` (DOM-id), `Dynamic`, `SelectVisible`.

### CGR — переименования по конвенции

| Файл | Шаг | Состояние |
|---|---|---|
| `CGR1_rename_dynamic_settings.md` | `ClayGridDynamicOptions` → `ClayGridDynamicSettings` | ✅ |

Действующее правило имён: `Clay*Options` — конфигурация экземпляра компонента на странице;
`Clay*Settings` — настройки уровня приложения (из конфигурации, через DI).

## Tree — к выполнению

| Файл | Шаг | Состояние |
|---|---|---|
| `CT1_tree_view_skeleton.md` | `ClayTreeView`: структура классов, ленивая загрузка уровней, тестовая страница | ✅ |

Редакция 2 учитывает итоги CGO и CGR1: вся конфигурация дерева — в `ClayTreeOptions`
(7 параметров тега), обязательный тест-защёлка на дефолты, эталоны разметки — `Home.razor`
и `MedicalTests.razor` после CGO. Перечень отличий от первой редакции — в §11 самого файла.

Дорожная карта CT2–CT8 (выделение, каскад, поиск, персистенция состояния в БД, контекстное
меню, drag-and-drop, виртуализация) — в §10 файла CT1. Отдельных промтов на них пока нет.
