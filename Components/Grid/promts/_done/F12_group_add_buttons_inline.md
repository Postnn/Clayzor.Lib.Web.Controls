# F12. Кнопки «добавить условие/группу» — в строку переключателя И/ИЛИ

Файл: `Components/Grid/Filter/ClayFilterGroup.razor`.
Чистая перестановка разметки, логику (`AddExpression`, `AddGroup`, `SetLogic`,
`OnRemove`) не трогаем.

## Что сейчас
Заголовок группы: переключатель И/ИЛИ + (для не-корня) кнопка удаления справа.
Кнопки «добавить условие» и «добавить группу» — отдельным блоком **под** дочерними узлами.

## Что нужно
Перенести обе кнопки добавления **в строку заголовка**, на один уровень с переключателем
И/ИЛИ данной группы. Нижний блок кнопок убрать.

## Правка `ClayFilterGroup.razor`
Строку заголовка сделать такой (кнопки добавления — сразу после переключателя,
кнопка удаления группы остаётся справа):
```razor
@* ── Заголовок группы: И/ИЛИ + добавление + удаление ── *@
<div style="display:flex;align-items:center;gap:6px;flex-wrap:wrap">
    <MudToggleGroup T="LogicalOperator"
                    Value="@Node.Logic"
                    ValueChanged="@SetLogic"
                    Color="Color.Primary"
                    Size="Size.Small"
                    CheckMark="false"
                    SelectionMode="SelectionMode.SingleSelection">
        <MudToggleItem Value="@LogicalOperator.And" Text="И" />
        <MudToggleItem Value="@LogicalOperator.Or" Text="ИЛИ" />
    </MudToggleGroup>

    <MudButton Variant="Variant.Text" Size="Size.Small"
               StartIcon="@Icons.Material.Filled.Add"
               OnClick="@AddExpression" Style="text-transform:none">
        добавить условие
    </MudButton>
    <MudButton Variant="Variant.Text" Size="Size.Small"
               StartIcon="@Icons.Material.Filled.CreateNewFolder"
               OnClick="@AddGroup" Style="text-transform:none">
        добавить группу
    </MudButton>

    <MudSpacer />

    @if (!IsRoot)
    {
        <MudIconButton Icon="@Icons.Material.Filled.Close"
                       Size="Size.Small"
                       OnClick="@OnRemove"
                       title="Удалить группу" />
    }
</div>
```
Затем **удалить** нижний блок:
```razor
@* ── Кнопки добавления ── *@
<div style="display:flex;gap:6px;flex-wrap:wrap"> … добавить условие / добавить группу … </div>
```
`flex-wrap:wrap` в заголовке оставить — на узкой ширине кнопки аккуратно перенесутся.

## Критерии
- [ ] Кнопки «добавить условие/группу» находятся в одной строке с переключателем И/ИЛИ соответствующей группы.
- [ ] Нижний отдельный блок этих кнопок отсутствует.
- [ ] Кнопка удаления группы (для не-корневых) по-прежнему справа в той же строке.
- [ ] Поведение добавления/удаления не изменилось; вложенные группы работают так же.
- [ ] `dotnet build` без ошибок.
