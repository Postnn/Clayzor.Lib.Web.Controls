# V14. Показать фильтр по значению в диалоге настраиваемого фильтра

В настраиваемом (составном) фильтре нет информации о том, что на колонку
установлен фильтр по значению. Причина: `ClayFilterDialog` клонирует весь
`_filterRoot` в `_draft` (узлы `ValueFilter` там есть и на «Применить»
сохраняются — не теряются), но `Filter/ClayFilterGroup.razor` перебирает
`Node.Nodes` и имеет ветки только для `ColumnFilter` и `ClayFilterGroupNode`;
`ValueFilter` не попадает ни в одну — поэтому не отображается.

Правка аддитивная, по образцу уже существующего показа условий из диалога колонки
(«Редактируется в диалоге колонки: …»): добавить ветку для `ValueFilter` —
read-only строку с описанием и кнопкой удаления.

Файл: `Components/Grid/Filter/ClayFilterGroup.razor`.

## Изменение
В цикле `@for (int i = 0; i < Node.Nodes.Count; i++)`, после ветки
`else if (node is ClayFilterGroupNode group) { … }`, добавить:
```razor
else if (node is ValueFilter vf)
{
    <div style="display:flex;align-items:center;gap:8px;padding:6px 8px;
                border:1px solid var(--mud-palette-lines-default);border-radius:4px;
                color:var(--mud-palette-text-secondary);font-size:var(--clay-font-size)">
        <MudIcon Icon="@Icons.Material.Filled.Checklist" Size="Size.Small" />
        <MudText Typo="Typo.body2" Style="flex:1 1 auto">
            Фильтр по значению: @ValueFilterLabel(vf)
        </MudText>
        <MudIconButton Icon="@Icons.Material.Filled.Close"
                       Size="Size.Small"
                       OnClick="@(() => RemoveNode(capturedIndex))"
                       title="Удалить фильтр по значению" Style="flex:0 0 auto" />
    </div>
}
```
В `@code` добавить хелпер подписи (формат как у чипа в трее — треб. V12):
```csharp
private string ValueFilterLabel(ValueFilter vf)
{
    var name = Columns.FirstOrDefault(c => c.SqlName == vf.Column)?.DisplayName ?? vf.Column;
    return $"{name}: выбраны значения";
}
```
`ValueFilter` в области видимости (тот же неймспейс `...Grid.Filter`). `RemoveNode`
уже есть и корректно удалит узел из `Node.Nodes`; на «Применить» вернётся `_draft`
без него.

## Замечания
- Строка **read-only** (как условия из диалога колонки): здесь фильтр по значению
  не редактируется, только отображается и удаляется. Менять состав значений —
  через диалог по значку колонки.
- Изменения в `ClayFilterDialog.razor(.cs)` не нужны: `_draft` — клон всего
  дерева, `Apply()` возвращает его целиком, поэтому `ValueFilter` сохраняются при
  редактировании прочих условий (данная правка лишь делает их видимыми и
  удаляемыми).
- `Reset` (возврат пустого корня) по-прежнему очищает всё, включая фильтры по
  значению — это ожидаемое поведение «сбросить».

## Проверка
- [ ] При открытом фильтре по значению на колонке в диалоге настраиваемого фильтра
      видна строка «Фильтр по значению: {Колонка}: выбраны значения».
- [ ] Крестик в этой строке удаляет фильтр по значению; «Применить» сохраняет
      остальные условия и снятие.
- [ ] Редактирование других условий в диалоге не сбрасывает фильтр по значению.
- [ ] `dotnet build` без ошибок.
