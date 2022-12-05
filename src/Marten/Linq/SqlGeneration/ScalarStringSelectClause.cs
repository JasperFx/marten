using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal;
using Marten.Linq.Fields;
using Marten.Linq.Parsing;
using Marten.Linq.QueryHandlers;
using Marten.Linq.Selectors;
using Weasel.Postgresql;

namespace Marten.Linq.SqlGeneration;

internal class ScalarStringSelectClause: ISelectClause, IScalarSelectClause, ISelector<string>
{
    public ScalarStringSelectClause(string field, string from)
    {
        FromObject = from;
        FieldName = field;
    }

    public ScalarStringSelectClause(IField field, string from)
    {
        FromObject = from;

        FieldName = field.TypedLocator;
    }

    public string FieldName { get; private set; }

    public ISelectClause CloneToOtherTable(string tableName)
    {
        return new ScalarStringSelectClause(FieldName, tableName);
    }


    public void ApplyOperator(string op)
    {
        FieldName = $"{op}({FieldName})";
    }

    public ISelectClause CloneToDouble()
    {
        throw new NotSupportedException();
    }

    public Type SelectedType => typeof(string);

    public string FromObject { get; }

    public void WriteSelectClause(CommandBuilder sql)
    {
        sql.Append("select ");
        sql.Append(FieldName);
        sql.Append(" from ");
        sql.Append(FromObject);
        sql.Append(" as d");
    }

    public string[] SelectFields()
    {
        return new[] { FieldName };
    }

    public ISelector BuildSelector(IMartenSession session)
    {
        return this;
    }

    public IQueryHandler<TResult> BuildHandler<TResult>(IMartenSession session, Statement statement,
        Statement currentStatement)
    {
        return LinqHandlerBuilder.BuildHandler<string, TResult>(this, statement);
    }

    public ISelectClause UseStatistics(QueryStatistics statistics)
    {
        return new StatsSelectClause<string>(this, statistics);
    }

    public string Resolve(DbDataReader reader)
    {
        if (reader.IsDBNull(0))
        {
            return null;
        }

        return reader.GetFieldValue<string>(0);
    }

    public async Task<string> ResolveAsync(DbDataReader reader, CancellationToken token)
    {
        if (await reader.IsDBNullAsync(0, token).ConfigureAwait(false))
        {
            return null;
        }

        return await reader.GetFieldValueAsync<string>(0, token).ConfigureAwait(false);
    }
}
