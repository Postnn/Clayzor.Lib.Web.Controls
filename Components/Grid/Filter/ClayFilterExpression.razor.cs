using Clayzor.Lib.Web.Controls.Components.Grid.ColumnTypes;
using Microsoft.AspNetCore.Components;

namespace Clayzor.Lib.Web.Controls.Components.Grid.Filter;

/// <summary>
/// Редактор одного листового условия составного фильтра (<see cref="ColumnFilter"/>, <c>Source=CompositeDialog</c>).
/// Отображает выбор колонки, оператора и редактор значения через <see cref="ClayFilterValueEditor"/>.
/// </summary>
public partial class ClayFilterExpression : ComponentBase
{
    // ── Параметры ──────────────────────────────────────────────────────────────

    /// <summary>Редактируемый листовой узел фильтра.</summary>
    [Parameter, EditorRequired]
    public ColumnFilter Node { get; set; } = null!;

    /// <summary>Список доступных для фильтрации колонок.</summary>
    [Parameter, EditorRequired]
    public IReadOnlyList<ClayColumnMeta> Columns { get; set; } = [];

    /// <summary>Необязательные варианты значений на колонку (SqlName → список).</summary>
    [Parameter]
    public IReadOnlyDictionary<string, IReadOnlyList<ClayFilterOption>>? LookupOptions { get; set; }

    /// <summary>Вызывается при любом изменении условия — родитель вызывает StateHasChanged.</summary>
    [Parameter]
    public EventCallback OnChanged { get; set; }

    /// <summary>Вызывается при нажатии кнопки удаления условия.</summary>
    [Parameter]
    public EventCallback OnRemove { get; set; }

    // ── Внутреннее состояние ───────────────────────────────────────────────────

    /// <summary>Дескриптор типа выбранной колонки; null если колонка не выбрана.</summary>
    private ColumnTypeDescriptor? _descriptor;

    /// <summary>Доступные операторы для текущей колонки.</summary>
    private IReadOnlyList<ColumnFilterOperator> _availableOperators = [];

    /// <summary>Варианты lookup для текущей колонки (если заданы).</summary>
    private IReadOnlyList<ClayFilterOption>? _options;

    /// <summary>Ключ ремоунта редактора значения — меняется при смене колонки/оператора.</summary>
    private int _valueKey;

    /// <summary>Автофокус редактора значения только после явной смены колонки/оператора.</summary>
    private bool _focusValue;

    // ── Жизненный цикл ────────────────────────────────────────────────────────

    /// <inheritdoc/>
    protected override void OnParametersSet()
    {
        RefreshDescriptor(Node.Column);
    }

    /// <inheritdoc/>
    protected override void OnAfterRender(bool firstRender)
    {
        // Свежедобавленное условие (например, перетаскиванием колонки в составной фильтр)
        // сразу фокусирует поле значения. StateHasChanged() запрашивает ещё один рендер —
        // сбрасываем флаг не здесь же, а на следующем вызове OnAfterRender, иначе
        // перерисовка, которую мы только что запросили, увидит уже сброшенный флаг.
        if (firstRender && Node.IsNew)
        {
            Node.IsNew = false;
            _valueKey++;
            _focusValue = true;
            StateHasChanged();
            return;
        }
        if (_focusValue) _focusValue = false;
    }

    // ── Обработчики ───────────────────────────────────────────────────────────

    /// <summary>
    /// Обрабатывает смену колонки: обновляет дескриптор и сбрасывает оператор/значение
    /// на значения по умолчанию для нового типа. Переносит фокус на редактор значения.
    /// </summary>
    private async Task OnColumnChanged(string sqlName)
    {
        Node.Column = sqlName;
        RefreshDescriptor(sqlName);

        // Сбрасываем оператор и значение при смене колонки
        Node.Operator = _descriptor?.DefaultOperator ?? ColumnFilterOperator.Contains;
        Node.Value    = null;

        _valueKey++;
        _focusValue = true;

        await OnChanged.InvokeAsync();
    }

    /// <summary>Обрабатывает смену оператора. Переносит фокус на редактор значения.</summary>
    private async Task OnOperatorChanged(ColumnFilterOperator op)
    {
        Node.Operator = op;

        _valueKey++;
        _focusValue = true;

        await OnChanged.InvokeAsync();
    }

    /// <summary>Обрабатывает изменение значения из <see cref="ClayFilterValueEditor"/>.</summary>
    private async Task OnValueChanged(object? value)
    {
        Node.Value = value;
        await OnChanged.InvokeAsync();
    }

    // ── Вспомогательные ───────────────────────────────────────────────────────

    /// <summary>
    /// Обновляет <see cref="_descriptor"/>, <see cref="_availableOperators"/> и <see cref="_options"/>
    /// по SQL-имени колонки.
    /// </summary>
    private void RefreshDescriptor(string sqlName)
    {
        var col = Columns.FirstOrDefault(c => c.SqlName == sqlName);
        _descriptor        = col?.Type;
        _availableOperators = _descriptor is not null
            ? _descriptor.Operators
            : [Node.Operator];
        _options = LookupOptions?.GetValueOrDefault(sqlName);
    }
}
