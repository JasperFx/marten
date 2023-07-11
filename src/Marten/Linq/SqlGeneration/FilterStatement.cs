using System;
using System.Linq;
using Marten.Internal;
using Marten.Linq.QueryHandlers;
using Marten.Linq.Selectors;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SqlGeneration;

internal class SelectCtidSelectClause: ISelectClause
{
    public SelectCtidSelectClause(Statement parent)
    {
        FromObject = parent.ExportName;
    }

    public void Apply(CommandBuilder builder)
    {
        builder.Append("select distinct ctid from ");
        builder.Append(FromObject);
        builder.Append(" as d");
    }

    public bool Contains(string sqlText)
    {
        return false;
    }

    public string FromObject { get; }
    public Type SelectedType { get; } = typeof(long);

    public string[] SelectFields()
    {
        return new[] { "ctid" };
    }

    public ISelector BuildSelector(IMartenSession session)
    {
        throw new NotSupportedException();
    }

    public IQueryHandler<T> BuildHandler<T>(IMartenSession session, ISqlFragment topStatement,
        ISqlFragment currentStatement)
    {
        throw new NotSupportedException();
    }

    public ISelectClause UseStatistics(QueryStatistics statistics)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
///     This is built to filter a child collection in a CTE query
/// </summary>
internal class FilterStatement: SelectorStatement
{
    public FilterStatement(IMartenSession session, Statement parent, ISqlFragment where)
    {
        if (where == null)
        {
            throw new ArgumentNullException(nameof(where));
        }

        Wheres.Add(where);

        SelectClause = new SelectCtidSelectClause(parent);

        ConvertToCommonTableExpression(session);

        parent.InsertAfter(this);

        // This *should* enable n-deep child collection queries. Let's hope.
        if (Wheres.Any())
        {
            compileAnySubQueries(session);
        }
    }
}
