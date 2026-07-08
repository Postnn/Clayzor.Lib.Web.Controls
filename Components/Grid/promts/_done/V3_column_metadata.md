# V3. Метаданные колонки: `AllowValueFilter` + подписи bool

Дать колонке возможность включить режим фильтра по значению и задать вручную
подписи для булевых значений (треб. 1 и 15). **Behavior-preserving**: новые поля
опциональны, старые вызовы работают без изменений.

## Файлы
- `Components/Grid/ClayColumnDef.razor` — новые `[Parameter]`.
- `Components/Grid/IClayGrid.cs` — поля в `ClayColumnMeta` + расширение
  сигнатуры `RegisterColumn`.
- `Components/Grid/ClayGrid.razor.cs` — реализация `RegisterColumn`
  (метод строит `ClayColumnMeta`, ищи по `_columnBySqlName[sqlName] = meta`).
- `Components/Grid/ClayColumn.razor` вызывать не нужно (регистрация идёт из
  `ClayColumnDef.OnInitialized`).

## `ClayColumnDef.razor` — добавить параметры
- `[Parameter] public bool AllowValueFilter { get; set; }` — включает фильтр по
  уникальному значению для этой колонки (при `Filterable=true`).
- `[Parameter] public string? BoolTrueLabel { get; set; }` — подпись значения
  `true` для булевой колонки (напр. «Только IT оборудование»). null → «Да».
- `[Parameter] public string? BoolFalseLabel { get; set; }` — подпись `false`
  (напр. «Не IT оборудование»). null → «Нет».
- Передать их в `Grid?.RegisterColumn(...)` в `OnInitialized`.

## `IClayGrid.ClayColumnMeta` — добавить init-поля
- `public bool AllowValueFilter { get; init; }`
- `public string? BoolTrueLabel { get; init; }`
- `public string? BoolFalseLabel { get; init; }`

## `RegisterColumn` — расширить сигнатуру (в интерфейсе и реализации)
Текущая:
`RegisterColumn(int columnId, string sqlName, string displayName, bool groupable, bool filterable, string? sortName = null)`
Добавить в конец **опциональные** параметры (чтобы не ломать существующие вызовы):
`bool allowValueFilter = false, string? boolTrueLabel = null, string? boolFalseLabel = null`.
Заполнить ими новые поля `ClayColumnMeta`.

## Глобальный тумблер грида (треб. 1 — «включать в настройках грида»)
- В `ClayGrid.razor.cs` добавить `[Parameter] public bool EnableValueFilter { get; set; } = true;`
  Значок/попап фильтра по значению в заголовке (задача V7) показывать только при
  `EnableValueFilter && meta.Filterable && meta.AllowValueFilter`.
- Тумблер в диалоге настройки колонок (`ClayColumnSettingsDialog`) — **не здесь**,
  это опциональная задача V8.

## Критерии
- [ ] Существующие вызовы `RegisterColumn` компилируются без правок (новые
      параметры опциональны).
- [ ] `ClayColumnMeta` несёт `AllowValueFilter`, `BoolTrueLabel`,
      `BoolFalseLabel`.
- [ ] `ClayColumnDef` прокидывает новые параметры в реестр.
- [ ] Добавлен грид-параметр `EnableValueFilter` (по умолчанию `true`), пока
      нигде не влияет на рендер (используется в V7).
- [ ] `dotnet build` без ошибок; поведение грида не изменилось.
