> Часть серии «GB — багфиксы по итогам тестирования». Перед началом прочитай **GB0_README_grid_ux_fixes.md** и **GF0_README_dynamic_fixes.md**. Делай ТОЛЬКО этот шаг, затем сборка + тест + блок «Проверка».

# GB14 — фильтр по уникальным значениям не включается через диалог настройки колонок (динамика)

Прочитать перед началом: `Components/Grid/ClayGrid.Dynamic.cs` — построение
`ClayColumnMeta` в `InitDynamicMode`/`BuildDynamicColumns` (ДВЕ ветки `new ClayColumnMeta`:
фильтр-онли Тип 6/11 и обычные `gridCols`); `Components/Grid/IClayGrid.cs` — `ClayColumnMeta`
(поле `AllowValueFilter` — `init`), `RegisterColumn`, `IsValueFilterAvailable`;
`Components/Grid/ClayGrid.Filtering.cs` — `IsValueFilterAvailable`, `_valueFilterDisabledColumns`,
`OpenValueFilterDialog`; `Components/Grid/ClayGrid.razor.cs` — `OpenColumnSettings` (как строится
`AllowValueFilter` у `ColumnSettingsItem`, ~строка 359; ветка Apply, где заполняется
`_valueFilterDisabledColumns`); `Components/Grid/ClayColumnDef.razor` — как СТАТИЧЕСКАЯ колонка
передаёт `AllowValueFilter` через `RegisterColumn` (образец);
`Clayzor.Lib.Web.Controls/Components/Grid/Dynamic/ClayColumnKind.cs` — типы колонок.

## Дефект

Переключатель «Фильтр по значению» в диалоге настройки колонок в динамическом гриде ничего не
включает. Причина — в модели доступности value-filter, которая рассчитана на статические колонки:

```csharp
// ClayGrid.Filtering.cs
bool IClayGrid.IsValueFilterAvailable(string sqlName)
{
    …
    if (!EnableValueFilter) return false;
    return meta.Filterable && meta.AllowValueFilter
           && !_valueFilterDisabledColumns.Contains(sqlName);   // ← гейт
}
```

Доступность требует `meta.AllowValueFilter == true`, а диалог умеет только **снимать** её
(добавляя колонку в `_valueFilterDisabledColumns`). Стартовое `AllowValueFilter` задаёт
регистрация колонки. В статике его передаёт разметка `ClayColumnDef` (`RegisterColumn(…,
allowValueFilter, …)`). В динамике `ClayColumnMeta` собирается вручную в `ClayGrid.Dynamic.cs`,
и **`AllowValueFilter` там не выставляется ни в одной из двух веток** → `init`-поле остаётся
`false` для всех динамических колонок.

Следствие цепочкой: `AllowValueFilter=false` у всех метаданных → `IsValueFilterAvailable`
всегда `false` → иконка value-filter в шапке колонки не появляется; а в диалоге настройки
`ColumnSettingsItem.AllowValueFilter` тоже стартует из
`!_valueFilterDisabledColumns.Contains(...) && m.AllowValueFilter` (`ClayGrid.razor.cs:359`) —
то есть всегда `false`, и переключатель, даже если его включить, при Apply максимум **не
добавит** колонку в disabled-множество, но включить value-filter не может: базовый флаг `false`.

Схема `ClayColumnDefinition` (`Запросы.Колонки`) отдельного признака «разрешить value-filter»
не имеет. Значит политика для динамики: value-filter доступен для тех же колонок, для которых
он осмыслен, — фильтруемых колонок обычных типов (не фильтр-онли, не тяжёлые типы вроде Html).
Пользователь при желании выключает его в диалоге (существующий механизм
`_valueFilterDisabledColumns`).

## Изменить/создать

`Components/Grid/ClayGrid.Dynamic.cs` — в ветке построения обычных `gridCols` (там, где
`Groupable = true`, `Filterable = col.Type != (int)ClayColumnKind.List`) задать
`AllowValueFilter` по типу колонки:

```csharp
var kind = (ClayColumnKind)col.Type;

// Value-filter (выбор из списка уникальных значений колонки) осмыслен для «плоских»
// значений. Исключаем: List (Тип 5 — уже справочник-подзапрос, фильтруется иначе),
// Html/Icon/Link (не атомарные значения), а также нефильтруемые колонки.
bool allowValueFilter =
    kind is ClayColumnKind.Number or ClayColumnKind.Text or ClayColumnKind.Date
         or ClayColumnKind.Bool   or ClayColumnKind.DateTimeLocal
         or ClayColumnKind.TimeLocal or ClayColumnKind.LimitedText;

var meta = new ClayColumnMeta
{
    ColumnId         = col.ColumnId,
    SqlName          = col.Column,
    DisplayName      = col.Header ?? col.Column,
    SortName         = col.Column,
    Groupable        = true,
    Filterable       = col.Type != (int)ClayColumnKind.List,
    AllowValueFilter = allowValueFilter,
    Type             = desc,
};
```

Список типов сверь с фактическим поведением диалога value-filter на реальных данных: если для
какого-то типа диалог уникальных значений заведомо непригоден (например, там, где хранится
длинный уникальный текст и список значений бессмыслен), убери его из набора и отметь в отчёте.
Фильтр-онли ветку (Тип 6/11) НЕ трогать — там `AllowValueFilter` остаётся `false` (эти колонки
в гриде не показываются, у них своя семантика условия).

Больше ничего менять не нужно: `IsValueFilterAvailable`, `_valueFilterDisabledColumns`, диалог
настройки и его Apply-ветка, `OpenValueFilterDialog`, `ClayColumnValueFilterDialog` — уже
работают, им не хватало только ненулевого стартового `AllowValueFilter`.

## Не делай

- Не выставляй `AllowValueFilter = true` безусловно всем динамическим колонкам — фильтр-онли
  (Тип 6/11), List, Html/Icon/Link не должны его получать. Гейт по типу обязателен.
- Не меняй сигнатуру/семантику `IsValueFilterAvailable` и не убирай гейт
  `_valueFilterDisabledColumns` — он реализует выключение пользователем (обратная операция),
  и в статике на нём же всё держится.
- Не делай `ClayColumnMeta.AllowValueFilter` мутабельным (`set` вместо `init`) — значение
  задаётся при построении, менять его после регистрации незачем; пользовательское выключение
  живёт в `_valueFilterDisabledColumns`, а не в мете.
- Не добавляй новое поле в `ClayColumnDefinition`/`Запросы.Колонки` ради флага value-filter —
  это изменение схемы БД и продуктовое решение, в багфикс не входит. Политика по типу колонки
  достаточна.
- Не трогай статический путь (`ClayColumnDef` → `RegisterColumn`) — там `AllowValueFilter`
  приходит из разметки и работает.
- Не меняй `EnableValueFilter` (глобальный `[Parameter]`, по умолчанию `true`).

## Проверка (ручная)

- `Kesco.App.Web.Inventory`, `?id=140`: у текстовой/числовой/датовой колонки в шапке появилась
  иконка фильтра по значению; клик → открылся диалог уникальных значений, выбор значений
  фильтрует грид;
- «Настройка колонок»: у этих колонок переключатель «Фильтр по значению» включён по умолчанию;
  выключить его для колонки → Apply → в шапке этой колонки иконки value-filter больше нет,
  у остальных осталась (`_valueFilterDisabledColumns` работает);
- включить обратно → Apply → иконка вернулась;
- колонка Тип 5 (List): value-filter недоступен (иконки нет, переключатель отражает
  недоступность) — фильтруется своим способом;
- колонка Тип 8 (Html) / Тип 4 (Link) / Тип 9 (Icon): value-filter недоступен;
- фильтр-онли колонка (Тип 6/11): в гриде не показана, в настройке ведёт себя как раньше;
- применить value-filter, затем групповую операцию «Выгрузка в Excel» → в файле отфильтрованные
  данные (value-filter учтён в `where`);
- value-filter + составной фильтр одновременно на разных колонках → взаимно учитываются;
- F5 после включения/выключения value-filter в диалоге → состояние сохранилось
  (persist колонок, GF12);
- статический режим (`/medical-tests`): value-filter работает как раньше (колонки с
  `AllowValueFilter="true"` в разметке);
- `dotnet build` + `dotnet test` — зелёные.
