using System.Globalization;
using System.Web;
using Clayzor.Lib.Web.Controls.Components.Filter;
using Clayzor.Lib.Web.Controls.Components.Grid.Dynamic;
using Dapper;

namespace Clayzor.Lib.Web.Controls.Components.Grid;

public partial class ClayGrid<TEntity> where TEntity : class
{
    private string? BuildDynamicFilter(ClayFilterGroupNode? filter, DynamicParameters parameters) =>
        BuildDynamicFilter(filter, _opt.DynamicContext, parameters, _dynamicKnownColumns);

    internal static string? BuildDynamicFilter(ClayFilterGroupNode? filter, ClayGridDynamicContext? context,
        DynamicParameters parameters, ISet<string> knownColumns)
    {
        if (context is null)
            return ClayCompositeSqlBuilder.Build(filter, parameters, knownColumns);

        if (!knownColumns.Contains(context.Column))
            throw new InvalidOperationException("В настройках запроса отсутствует колонка контекста списка.");

        var scoped = new ClayFilterGroupNode
        {
            Nodes = [new ColumnFilter { Column = context.Column, Operator = ColumnFilterOperator.Equals, Value = context.Value }],
        };
        var userFilter = CloneFilterTreeWithoutColumn(filter, context.Column);
        if (userFilter is not null)
            scoped.Nodes.Add(userFilter);

        return ClayCompositeSqlBuilder.Build(scoped, parameters, knownColumns);
    }

    internal static string BuildContextCreateUrl(Uri url, ClayGridDynamicContext context)
    {
        var target = new UriBuilder(url);
        var query = HttpUtility.ParseQueryString(target.Query);
        // Старый default с подчёркиванием не должен перебить выбор вызывающей страницы.
        query.Remove("_" + context.CreateParameter);
        query[context.CreateParameter] = context.Value.ToString(CultureInfo.InvariantCulture);
        target.Query = query.ToString();
        return target.Uri.AbsoluteUri;
    }
}
