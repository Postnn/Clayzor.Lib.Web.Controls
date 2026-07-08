using Clayzor.Lib.Web.Controls.Components.Grid.ColumnTypes;
using Microsoft.AspNetCore.Components;

namespace Clayzor.Lib.Web.Controls.Components.Grid.Filter;

/// <summary>
/// Единый редактор значения фильтра по типу колонки.
/// Переиспользуется в <c>ClayColumnFilterDialog</c> и в будущем диалоге составного фильтра (задача 09).
/// При операторах без значения (IsEmpty/IsNotEmpty/IsNull/IsNotNull) скрывается и обнуляет <see cref="Value"/>.
/// </summary>
public partial class ClayFilterValueEditor : ComponentBase
{
    // ── Параметры ──────────────────────────────────────────────────────────────

    /// <summary>Дескриптор типа колонки — определяет рендеримый контрол и операторы.</summary>
    [Parameter, EditorRequired]
    public ColumnTypeDescriptor Type { get; set; } = null!;

    /// <summary>Текущее значение (object?, типизируется адаптером через <see cref="Type"/>).</summary>
    [Parameter]
    public object? Value { get; set; }

    /// <summary>Callback обновления значения для двустороннего биндинга.</summary>
    [Parameter]
    public EventCallback<object?> ValueChanged { get; set; }

    /// <summary>
    /// Необязательный список вариантов для выпадающего выбора значения.
    /// При наличии — рендерится <c>MudSelect</c> вместо типозависимого поля.
    /// </summary>
    [Parameter]
    public IReadOnlyList<ClayFilterOption>? Options { get; set; }

    /// <summary>Текущий оператор. Если оператор не требует значения — редактор скрыт.</summary>
    [Parameter]
    public ColumnFilterOperator Operator { get; set; }

    /// <summary>Устанавливать ли фокус при появлении редактора (AutoFocus на контроле).</summary>
    [Parameter]
    public bool AutoFocus { get; set; }

    // ── Внутреннее состояние ───────────────────────────────────────────────────

    /// <summary>Признак того, что оператор требует значения (редактор видим).</summary>
    private bool _takesValue;

    /// <summary>
    /// Типизированное значение, синхронизированное с <see cref="Value"/> через <see cref="Type"/>.
    /// Хранит распарсенное значение нужного CLR-типа.
    /// </summary>
    private object? _typedValue;

    // ── Жизненный цикл ────────────────────────────────────────────────────────

    /// <inheritdoc/>
    protected override void OnParametersSet()
    {
        _takesValue = Type.OperatorTakesValue(Operator);

        if (_takesValue)
        {
            // Синхронизируем _typedValue из внешнего Value через дескриптор
            _typedValue = Options is not null
                ? Value
                : Type.Parse(Value?.ToString());
        }
        else
        {
            // Оператор без значения — сбрасываем value, если оно было установлено
            if (Value is not null)
            {
                _typedValue = null;
                // Уведомляем родителя об обнулении, не ждём ввода пользователя
                _ = ValueChanged.InvokeAsync(null);
            }
        }
    }

    // ── Обработчики ───────────────────────────────────────────────────────────

    /// <summary>
    /// Вызывается при изменении типизированного поля (Text/Number/Decimal/Date/Boolean).
    /// Конвертирует значение обратно в <c>object?</c> и уведомляет родителя.
    /// </summary>
    private async Task OnTypedChanged(object? typedVal)
    {
        _typedValue = typedVal;
        await ValueChanged.InvokeAsync(typedVal);
    }

    /// <summary>
    /// Вызывается при изменении выпадающего списка вариантов (lookup).
    /// Значение передаётся напрямую без преобразования дескриптором.
    /// </summary>
    private async Task OnLookupChanged(object? val)
    {
        _typedValue = val;
        await ValueChanged.InvokeAsync(val);
    }
}
