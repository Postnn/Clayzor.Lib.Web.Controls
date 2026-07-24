# 06. Разбиение ClayGridPageBase на partial-классы

`ClayGridPageBase.cs` — 1157 строк, смешивает загрузку, экспорт, печать, вывод
типов. Разнести по partial-файлам **без изменения поведения**. Все файлы:
`public partial class ClayGridPageBase<T> ...` — повторить точную сигнатуру и
ограничения (`where T : ...`); базовый класс/интерфейсы — только в основном файле.

## Раскладка (методы реальные)
`ClayGridPageBase.cs` (ядро):
- поля `_query`, `Grid`, `[Inject]` (`Db`, `Snackbar`, `DialogService`, `JS`), `_rows`, `_loading`, success-сообщения;
- `OnAfterRenderAsync`; загрузка `LoadData`, `LoadFlatData`, `LoadGroupedData`;
- `ToggleGroup`, `OpenAddDialog`.

`ClayGridPageBase.ColumnTypes.cs` (вывод типов; согласовать с задачей 03):
- `_idColumnName`, `_propertyMap`, `_inferredColumnTypes`, `FilterColumnTypes`,
  `GetIdColumnName`, `BuildPropertyMap`, `InferFilterColumnTypes`, `MapClrTypeToColumnType`.

`ClayGridPageBase.Export.Excel.cs`:
- `IClayGridDataLoader.ExcelExportAsync`, `BuildAllRowsForExcel`,
  `BuildAllGroupedRowsForExcel`, `BuildExportRows`, `CollectCounts`, `SanitizeFileName`.

`ClayGridPageBase.Export.Print.cs`:
- `BuildAllRowsForPrint`, `BuildAllFlatRowsForPrint`, `BuildAllGroupedRowsForPrint`.

`ClayGridPageBase.Export.Selected.cs`:
- `BuildAllRowsForSelected`, `BuildAllFlatRowsForSelected`, `BuildAllGroupedRowsForSelected`,
  `GetGroupKeysByDepth`, `CollectKeysByDepth`.

После каждого блока — `dotnet build`. Перемещать вырезанием, без правок тела.

## Критерии
- [ ] Поведение идентично; основной файл < ~350 строк; каждый partial — одна тема.
- [ ] `dotnet build` без ошибок.

## Необязательно (НЕ здесь)
`BuildAll*ForPrint/Excel/Selected` дублируются — можно свести к одному
параметризованному построителю, но это изменение поведения; отдельно, с тестами.
