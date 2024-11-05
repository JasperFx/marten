#nullable enable
using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal;
using Marten.Linq.Members;
using Marten.Linq.Parsing;
using Marten.Linq.QueryHandlers;
using Marten.Linq.Selectors;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SqlGeneration;

internal class ScalarStringSelectClause: ISelectClause, IScalarSelectClause, ISelector<string>
{
    public ScalarStringSelectClause(string field, string from)
    {
        FromObject = from;
        MemberName = field;
    }

    public ScalarStringSelectClause(IQueryableMember field, string from)
    {
        FromObject = from;

        MemberName = field.TypedLocator;
    }

    public string MemberName { get; private set; }

    public ISelectClause CloneToOtherTable(string tableName)
    {
        return new ScalarStringSelectClause(MemberName, tableName);
    }


    public void ApplyOperator(string op)
    {
        MemberName = $"{op}({MemberName})";
    }

    public ISelectClause CloneToDouble()
    {
        throw new NotSupportedException();
    }

    public Type SelectedType => typeof(string);

    public string FromObject { get; }

    public void Apply(IPostgresqlCommandBuilder sql)
    {
        sql.Append("select ");
        sql.Append(MemberName);
        sql.Append(" as data from ");
        sql.Append(FromObject);
        sql.Append(" as d");
    }

    public string[] SelectFields()
    {
        return new[] { MemberName };
    }

    public ISelector BuildSelector(IMartenSession session)
    {
        return this;
    }

    public IQueryHandler<TResult> BuildHandler<TResult>(IMartenSession session, ISqlFragment statement,
        ISqlFragment currentStatement)
    {
        return LinqQueryParser.BuildHandler<string, TResult>(this, statement);
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

    public override string ToString()
    {
        return $"Select string value from {FromObject}";
    }
}
