# 05. Разбиение code-behind грида по темам

После задачи 04 вся логика в `ClayGrid.razor.cs`. Разносим по partial-файлам по
темам — **без изменения поведения**. Все файлы: `public partial class ClayGrid<TEntity>`
(база/интерфейсы — только в одном, основном). Поля переносить вместе с темой.

## Раскладка (имена реальные из @code)
- `ClayGrid.Search.cs` — `_searchText` + обработчики поиска.
- `ClayGrid.Sorting.cs` — `_sortState`, `ToggleSort`, `HandleSortClick`, `GetSortBadge`.
- `ClayGrid.Grouping.cs` — `_groupColumns`, `_groupChildIds`, `AddGroupColumn`,
  `GroupColumns`, `OnGroupTriToggle`, `OnHeaderTriToggle`.
- `ClayGrid.Filtering.cs` — `_activeFilters`, `_filterTrayExpanded`, `OpenFilterDialog`,
  `OnFilterTrayDragOver`, `OnFilterTrayDrop`, `AddFilterAsync`, `BuildFilterDescription`.
  > Этот файл будет переписан задачами 10–11 (переход на дерево фильтра) — выделение
  > в отдельный файл упрощает ту замену.
- `ClayGrid.DragDrop.cs` — `_dragSourceIndex`, `_trayExpanded`, `OnChipDragStart`,
  `OnChipDragEnd`, `OnTrayDragOver`, `OnTrayDrop`.
- `ClayGrid.Selection.cs` — `_selectMode`, `_selectAllChecked`, `_selectedIds`,
  `OnRowSelectAsync`, выбор всех.
- `ClayGrid.ExportMenu.cs` — `_isExporting`, `_openSubGroups`, `ToggleSubGroup`,
  `Print{CurrentPage,Selected,All}Internal`, `Excel{CurrentPage,Selected,All}Internal`.
- `ClayGrid.Paging.cs` — `_pageNumber`, `_pageSize`, обработчики пагинации.

После каждого файла — `dotnet build`. Перемещать вырезанием, без правок тела.

## Критерии
- [ ] Поведение идентично; каждый partial — одна тема.
- [ ] `dotnet build` без ошибок.

## Необязательно (позже, рискованнее)
Выделить из разметки под-компоненты (`ClayGridToolbar`, `ClayGridChipTray`,
`ClayGridFilterTray`, `ClayGridHeaderRow`, `ClayGridGroupHeader`) с параметрами и
`EventCallback`. Это меняет разметку → отдельно, маленькими шагами, с проверкой.
