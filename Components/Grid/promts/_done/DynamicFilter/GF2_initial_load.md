> Часть серии «Багфиксы динамического режима ClayGrid». Перед началом прочитай **GF0_README_dynamic_fixes.md** и **_readme_grid_dynamic.md**. Требует выполненного **GF1**. Делай ТОЛЬКО этот шаг, затем сборка + тест + блок «Проверка».

# GF2 — первая загрузка данных в динамическом режиме

Прочитать перед началом: `Components/Grid/ClayGrid.Dynamic.cs` — `InitDynamicMode`,
`LoadDynamicData`, `RestoreDynamicState`, `ApplyUrlParams`; `Components/Grid/ClayGrid.razor.cs` —
`OnInitialized`, `OnParametersSet`, `OnAfterRenderAsync`, `NotifyQueryChanged` (ветка
`if (Dynamic)`); `Components/Grid/ClayGridPageBase.cs` — как первую загрузку делает страница
в статическом режиме; `Kesco.App.Web.Inventory/Components/Pages/Home.razor`.

## Дефект

`InitDynamicMode()` заканчивается так:

```csharp
    ApplyUrlParams(opt);

    _dynamicInitDone = true;
}
```

и на этом всё. `NotifyQueryChanged()` (единственный путь к `LoadDynamicData`) в динамическом
режиме не вызывает никто:

- в статическом режиме первую загрузку инициирует страница-наследник `ClayGridPageBase`;
- в динамическом режиме страница — это `<ClayGrid Dynamic="true" />` и больше ничего,
  `DataLoader` не передан;
- `OnAfterRenderAsync(firstRender)` только выставляет `_columnsReady` и дёргает `StateHasChanged`.

Дальше по цепочке: `TotalCount` остаётся `0` → `_totalPages` возвращает `1` → в `ClayGrid.razor`
у всех четырёх кнопок пагинатора условие `Disabled="@(_pageNumber <= 1)"` /
`Disabled="@(_pageNumber >= _totalPages)"` истинно → «по страницам не переходит». Это следствие
пустого `TotalCount`, а не отдельный дефект пагинации.

## Изменить/создать

`ClayGrid.Dynamic.cs`, хвост `InitDynamicMode()`:

```csharp
    // Применить URL-параметры (фильтры и колонки)
    ApplyUrlParams(opt);

    _dynamicInitDone = true;

    // Первая загрузка: в динамическом режиме страницы-загрузчика нет,
    // грид обязан стартовать сам.
    await NotifyQueryChanged();
}
```

Место вызова важно:

- **после** `RestoreDynamicState` и `ApplyUrlParams` — иначе в первый запрос не попадут
  восстановленные фильтр, сортировка, группировка и `_pageSize`;
- **после** `_dynamicInitDone = true` — флаг закрывает повторный вход.

Ранние выходы `InitDynamicMode` (`gridId == 0`, `_dynamicDef is null`) загрузку не вызывают —
грузить нечего.

## Не делай

Не вызывай `LoadDynamicData` напрямую — только через `NotifyQueryChanged()`, он собирает
`ClayDataQuery` из текущего состояния и содержит ветку `if (Dynamic)`. Не переноси загрузку в
`OnAfterRenderAsync` — при пререндере (`<Routes @rendermode="InteractiveServer" />`) он не
сработает на серверном проходе. Не трогай статический режим: `DataLoader`/`OnQueryChanged`
остаются как были.

## Проверка (ручная)

- `?id=140` → грид заполнен данными СРАЗУ при открытии, без нажатия «Обновить»;
- «Всего: N записей» совпадает с `SELECT COUNT(*)` по `SQL` из `Запросы` для этого грида;
- «Стр. 1 из K», где `K = ceil(N / размер страницы)`;
- кнопки «Вперёд» и «В конец» активны (сам переход проверяется в GF3);
- в SQL-профайлере при открытии страницы видно: чтение определения, чтение колонок, чтение
  параметров пользователя, затем ОДИН paged-SELECT и ОДИН COUNT — не больше;
- `?id=140&CLID=7` c ранее сохранённой сортировкой → первый же запрос уходит с нужным
  `ORDER BY`, а не с дефолтным.
