# V5. Пресет оператора в `ClayColumnFilterDialog` (`InitialOperator`)

Мелкий enabler для треб. 7: при клике на пункт из контекстного списка условий
(«содержит…», «равно…», «начинается с…» и т.п.) в диалоге значений (V6) должна
открыться существующая форма фильтра по условию **с уже выбранным оператором**.

## Файл
`Components/Grid/ClayColumnFilterDialog.razor` (единственный файл, @code внутри).

## Изменения
- Добавить параметр:
  `[Parameter] public ColumnFilterOperator? InitialOperator { get; set; }`
- В `OnParametersSet()` в ветке «новый фильтр» (`ExistingFilter is null`):
  сейчас `_op1 = _descriptor.DefaultOperator;`. Заменить на:
  `_op1 = InitialOperator is { } io && _availableOperators.Contains(io) ? io : _descriptor.DefaultOperator;`
- Если `ExistingFilter is not null` — `InitialOperator` игнорируется (режим
  редактирования уже задаёт оператор).
- Больше ничего не менять: список операторов, второе условие, `Apply`,
  `GetFilterDescription` — как есть.

## Критерии
- [ ] Новый необязательный параметр `InitialOperator`.
- [ ] При новом фильтре и валидном `InitialOperator` первое условие открывается
      с ним; иначе — прежний `DefaultOperator`.
- [ ] Режим редактирования (`ExistingFilter != null`) не изменился.
- [ ] `dotnet build` без ошибок.
