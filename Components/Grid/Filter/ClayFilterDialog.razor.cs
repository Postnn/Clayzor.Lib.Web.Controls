using Clayzor.Lib.Web.Controls.Components.Grid.ColumnTypes;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Clayzor.Lib.Web.Controls.Components.Grid.Filter;

/// <summary>
/// Диалог настраиваемого (составного) фильтра.
/// Работает с глубокой копией входного дерева — оригинал не изменяется до нажатия «Применить».
/// «Применить» → возвращает изменённую копию через <c>DialogResult.Ok</c>.
/// «Сбросить»  → возвращает пустой корневой узел.
/// «Отмена»    → <c>DialogResult.Cancel</c>.
/// </summary>
public partial class ClayFilterDialog : ComponentBase
{
    // ── Параметры ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Каскадный параметр экземпляра диалога MudBlazor.
    /// Используется для закрытия диалога с результатом.
    /// </summary>
    [CascadingParameter]
    private IMudDialogInstance? MudDialog { get; set; }

    /// <summary>
    /// Корневой узел дерева фильтра. Диалог работает с глубокой копией,
    /// не изменяя оригинал до подтверждения.
    /// </summary>
    [Parameter, EditorRequired]
    public ClayFilterGroupNode Root { get; set; } = null!;

    /// <summary>Список доступных для фильтрации колонок.</summary>
    [Parameter, EditorRequired]
    public IReadOnlyList<ClayColumnMeta> Columns { get; set; } = [];

    /// <summary>Необязательные варианты значений на колонку (SqlName → список).</summary>
    [Parameter]
    public IReadOnlyDictionary<string, IReadOnlyList<ClayFilterOption>>? LookupOptions { get; set; }

    // ── Внутреннее состояние ───────────────────────────────────────────────────

    /// <summary>Глубокая копия входного корня — черновик редактирования.</summary>
    private ClayFilterGroupNode _draft = null!;

    /// <summary>Текстовое описание текущего черновика фильтра.</summary>
    private string _draftDescription = "";

    /// <summary>Резолвит отображаемое имя колонки по её SQL-имени.</summary>
    private Func<string, string> DisplayNameOf => sql =>
        Columns.FirstOrDefault(c => c.SqlName == sql)?.DisplayName ?? sql;

    // ── Жизненный цикл ────────────────────────────────────────────────────────

    /// <inheritdoc/>
    protected override void OnParametersSet()
    {
        // Создаём копию при первичной инициализации параметров.
        // Повторные изменения Root снаружи игнорируются — диалог изолирован.
        _draft ??= (ClayFilterGroupNode)Root.Clone();
        RecalcDescription();
    }

    // ── Обработчики ───────────────────────────────────────────────────────────

    /// <summary>
    /// Обрабатывает любые изменения в дереве черновика — вызывает перерисовку.
    /// </summary>
    private void OnDraftChanged()
    {
        RecalcDescription();
        StateHasChanged();
    }

    /// <summary>Пересчитывает текстовое описание черновика фильтра.</summary>
    private void RecalcDescription() =>
        _draftDescription = ClayFilterDescriptionBuilder.BuildText(
            _draft, DisplayNameOf,
            sql => Columns.FirstOrDefault(c => c.SqlName == sql)) ?? "";

    /// <summary>
    /// Применяет фильтр: отбрасывает незавершённые листья (без колонки), чистит
    /// опустевшие группы, закрывает диалог и возвращает черновик как результат.
    /// </summary>
    private void Apply()
    {
        StripIncompleteLeaves(_draft);
        MudDialog?.Close(DialogResult.Ok(_draft));
    }

    /// <summary>
    /// Рекурсивно удаляет из дерева листья с пустой колонкой и группы,
    /// оставшиеся без узлов после чистки. Лист без колонки — это нажатое
    /// «Добавить условие», в котором пользователь не выбрал колонку.
    /// </summary>
    private static void StripIncompleteLeaves(ClayFilterGroupNode group)
    {
        for (int i = group.Nodes.Count - 1; i >= 0; i--)
        {
            switch (group.Nodes[i])
            {
                case ColumnFilter cf when string.IsNullOrEmpty(cf.Column):
                    group.Nodes.RemoveAt(i);
                    break;
                case ClayFilterGroupNode sub:
                    StripIncompleteLeaves(sub);
                    if (sub.Nodes.Count == 0)
                        group.Nodes.RemoveAt(i);
                    break;
            }
        }
    }

    /// <summary>
    /// Сбрасывает фильтр: закрывает диалог и возвращает пустую группу.
    /// </summary>
    private void Reset() => MudDialog?.Close(DialogResult.Ok(new ClayFilterGroupNode()));

    /// <summary>Отменяет редактирование без изменений.</summary>
    private void Cancel() => MudDialog?.Cancel();
}
