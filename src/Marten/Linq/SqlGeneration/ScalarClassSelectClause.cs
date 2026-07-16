#nullable enable
using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using Marten.Internal;
using Marten.Linq.Members;
using Marten.Linq.Parsing;
using Marten.Linq.QueryHandlers;
using Marten.Linq.Selectors;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SqlGeneration;

// Reference-type counterpart to ScalarSelectClause<T>. That type is `where T : struct` because it
// also implements ISelector<T?> via System.Nullable<T>, which can't be closed over a reference type
// like byte[]. Reference types are already naturally nullable, so this skips the Nullable<T> wrapper
// entirely rather than trying to unify the two behind one generic (T? on an unconstrained T is only a
// compile-time nullable-reference annotation, not an actual Nullable<T> at runtime).
internal class ScalarClassSelectClause<T>: ISelectClause, IScalarSelectClause, ISelector<T> where T : class
{
    public ScalarClassSelectClause(string locator, string from)
    {
        FromObject = from;
        MemberName = locator;
    }

    public ScalarClassSelectClause(IQueryableMember field, string from)
    {
        FromObject = from;

        MemberName = field.TypedLocator;
    }

    public string MemberName { get; private set; }

    public ISelectClause CloneToOtherTable(string tableName)
    {
        return new ScalarClassSelectClause<T>(MemberName, tableName);
    }

    public void ApplyOperator(string op)
    {
        MemberName = $"{op}({MemberName})";
    }

    public ISelectClause CloneToDouble()
    {
        throw new NotSupportedException();
    }

    public Type SelectedType => typeof(T);

    public string FromObject { get; }

    public void Apply(ICommandBuilder sql)
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

    public ISelector BuildSelector(IStorageSession session)
    {
        return this;
    }

    public IQueryHandler<TResult> BuildHandler<TResult>(IStorageSession session, ISqlFragment statement,
        ISqlFragment currentStatement) where TResult : notnull
    {
        return LinqQueryParser.BuildHandler<T, TResult>(this, statement);
    }

    public ISelectClause UseStatistics(QueryStatistics statistics)
    {
        return new StatsSelectClause<T>(this, statistics);
    }

    public T Resolve(DbDataReader reader)
    {
        if (reader.IsDBNull(0))
        {
            // ISelector<T> declares a non-nullable T, but a null database value is a legitimate
            // result for a reference-type scalar (e.g. a nullable bytea column) and callers are
            // expected to tolerate it, matching ScalarStringSelectClause's contract for string.
            return null!;
        }

        return reader.GetFieldValue<T>(0);
    }

    public async Task<T> ResolveAsync(DbDataReader reader, CancellationToken token)
    {
        if (await reader.IsDBNullAsync(0, token).ConfigureAwait(false))
        {
            return null!;
        }

        return await reader.GetFieldValueAsync<T>(0, token).ConfigureAwait(false);
    }

    public override string ToString()
    {
        return $"Select {typeof(T).ShortNameInCode()} from {FromObject}";
    }
}
