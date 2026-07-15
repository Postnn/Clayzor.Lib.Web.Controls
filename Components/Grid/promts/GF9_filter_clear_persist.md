> Часть серии «Багфиксы динамического режима ClayGrid». Перед началом прочитай **GF0_README_dynamic_fixes.md** и **_readme_grid_dynamic.md**. Делай ТОЛЬКО этот шаг, затем сборка + тест + блок «Проверка».

# GF9 — снятый пользователем фильтр должен затираться в БД

Прочитать перед началом: `Components/Grid/ClayGrid.Dynamic.cs` — `SaveDynamicState`,
`RestoreDynamicState`; `Components/Grid/Dynamic/GridStateSerializer.cs` — `SerializeFilter`,
`DeserializeFilter`; `Clayzor.Lib.Entities/DynamicGrid/ClayGridUserParamsData.cs` — `SaveAsync`,
`LoadAsync`; `Components/Grid/ClayGrid.Filtering.cs` — `_filterRoot`, сброс фильтра.

## Дефект

`SaveDynamicState`:

```csharp
if (fltVal is not null && !_dynamicForcedParamNames.Contains(p(opt.FilterParamPrefix)))
    await ClayGridUserParamsData.SaveAsync(Db, _dynamicClid, p(opt.FilterParamPrefix), fltVal, t, s);
```

а `GridStateSerializer.SerializeFilter` возвращает `null` для пустого дерева:

```csharp
if (root is null || root.Nodes.Count == 0)
    return null;
```

Значит, когда пользователь снимает все условия, `flt+gridId` в `vwНастройки` не
перезаписывается — там остаётся прошлый JSON. После F5 `RestoreDynamicState` его читает,
`DeserializeFilter` возвращает дерево, и снятый фильтр возвращается сам собой. Снять фильтр
насовсем в динамическом режиме невозможно.

Четыре остальных параметра (`cols`/`srt`/`grp`/`pgs`) записываются всегда, включая пустую
строку, — там дефекта нет. `flt` выпадает из общего правила из-за `null`-семантики
`SerializeFilter`.

## Изменить/создать

`ClayGrid.Dynamic.cs`, `SaveDynamicState` — писать пустую строку вместо пропуска:

```csharp
// SerializeFilter отдаёт null для пустого дерева. Пропускать запись нельзя:
// снятый пользователем фильтр остался бы в БД и вернулся после перезагрузки.
var fltVal = GridStateSerializer.SerializeFilter(_filterRoot) ?? string.Empty;

...

if (!_dynamicForcedParamNames.Contains(p(opt.FilterParamPrefix)))
    await ClayGridUserParamsData.SaveAsync(Db, _dynamicClid, p(opt.FilterParamPrefix), fltVal, t, s);
```

`RestoreDynamicState` трогать не нужно — там уже:

```csharp
var root = GridStateSerializer.DeserializeFilter(fltVal);
if (root is not null)
    _filterRoot = root;
```

`DeserializeFilter("")` возвращает `null` (проверка `string.IsNullOrWhiteSpace`), `_filterRoot`
остаётся нетронутым. **Убедись в этом чтением кода, не на память.**

Сигнатуру `SerializeFilter` не меняй — `null` для пустого дерева используется как признак
«нечего сериализовать», и на неё завязаны тесты TG6.

## Не делай

Не меняй `SerializeFilter`/`DeserializeFilter`. Не трогай логику forced-параметров
(`_dynamicForcedParamNames`) — фильтр, пришедший из URL без `_`, по-прежнему не сохраняется.
Не трогай статический режим. Экономию записи (писать только изменившееся) НЕ добавляй — это GF12.

## Проверка (ручная)

- `?id=140&CLID=7` → навесить фильтр по «Тип исследования», F5 → фильтр восстановлен, бейдж
  на кнопке фильтра показывает число условий;
- снять все условия (кнопка сброса в диалоге настраиваемого фильтра), дождаться перезагрузки
  данных, F5 → фильтра НЕТ, бейдж пуст, грид показывает все записи;
- в `vwНастройки` строка `flt140` для `КодНастройкиКлиента = 7` существует со значением `''`
  (не удалена, не со старым JSON);
- `?id=140&CLID=7&type=5` (forced-фильтр из URL) → фильтр применён, но в БД `flt140` не
  перезаписан значением из URL.
