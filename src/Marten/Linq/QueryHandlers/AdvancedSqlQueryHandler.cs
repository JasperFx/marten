#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using Marten.Internal;
using Marten.Internal.Storage;
using Marten.Linq.Selectors;
using Marten.Linq.SqlGeneration;
using Weasel.Postgresql;
using System.Diagnostics.CodeAnalysis;

namespace Marten.Linq.QueryHandlers;

[UnconditionalSuppressMessage("Trimming", "IL2026",
    Justification = "Class-level: consumes RUC-annotated members (ISerializer, JasperFx.Events aggregator graph, CloseAndBuildAs / GenericFactoryCache fallbacks, FastExpressionCompiler). Document/event/projection types flow in from StoreOptions / Schema.For<T>() / projection registration and are preserved per the AOT publishing guide; AOT consumers supply a source-generator-backed serializer + pre-generated codegen artifacts.")]
[UnconditionalSuppressMessage("Trimming", "IL2090",
    Justification = "Class-level: generic class type-argument flow on the aggregator / storage instantiation. Types preserved at the projection-registration boundary.")]
[UnconditionalSuppressMessage("AOT", "IL3050",
    Justification = "Class-level: uses Type.MakeGenericType / MethodInfo.MakeGenericMethod / Activator.CreateInstance / FastExpressionCompiler — runtime code generation. AOT consumers pre-generate codegen artifacts (codegen write) and supply source-generator-backed serializer impls per the AOT publishing guide.")]
internal class AdvancedSqlQueryHandler<T>: AdvancedSqlQueryHandlerBase<T>, IQueryHandler<IReadOnlyList<T>>
{
    public AdvancedSqlQueryHandler(IStorageSession session, char placeholder, string sql, object[] parameters): base(placeholder, sql, parameters)
    {
        RegisterResultType<T>(session);
    }

    public override async IAsyncEnumerable<T> EnumerateResults(DbDataReader reader,
        [EnumeratorCancellation] CancellationToken token)
    {
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            yield return await ((ISelector<T>)Selectors[0]).ResolveAsync(reader, token).ConfigureAwait(false);
        }
    }
}

internal class AdvancedSqlQueryHandler<T1, T2>: AdvancedSqlQueryHandlerBase<(T1, T2)>, IQueryHandler<IReadOnlyList<(T1, T2)>> where T2 : notnull where T1 : notnull
{
    public AdvancedSqlQueryHandler(IStorageSession session, char placeholder, string sql, object[] parameters) : base(placeholder, sql, parameters)
    {
        RegisterResultType<T1>(session);
        RegisterResultType<T2>(session);
    }

    public override async IAsyncEnumerable<(T1, T2)> EnumerateResults(DbDataReader reader,
        [EnumeratorCancellation] CancellationToken token)
    {
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            var item1 = await ReadNestedRowAsync<T1>(reader, 0, token).ConfigureAwait(false);
            var item2 = await ReadNestedRowAsync<T2>(reader, 1, token).ConfigureAwait(false);
            yield return (item1, item2);
        }
    }
}
internal class AdvancedSqlQueryHandler<T1, T2, T3>: AdvancedSqlQueryHandlerBase<(T1, T2, T3)>, IQueryHandler<IReadOnlyList<(T1, T2, T3)>> where T1 : notnull where T2 : notnull where T3 : notnull
{
    public AdvancedSqlQueryHandler(IStorageSession session, char placeholder, string sql, object[] parameters) : base(placeholder, sql, parameters)
    {
        RegisterResultType<T1>(session);
        RegisterResultType<T2>(session);
        RegisterResultType<T3>(session);
    }

    public override async IAsyncEnumerable<(T1, T2, T3)> EnumerateResults(DbDataReader reader,
        [EnumeratorCancellation] CancellationToken token)
    {
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            var item1 = await ReadNestedRowAsync<T1>(reader, 0, token).ConfigureAwait(false);
            var item2 = await ReadNestedRowAsync<T2>(reader, 1, token).ConfigureAwait(false);
            var item3 = await ReadNestedRowAsync<T3>(reader, 2, token).ConfigureAwait(false);
            yield return (item1, item2, item3);
        }
    }
}

[UnconditionalSuppressMessage("Trimming", "IL2026",
    Justification = "Class-level: consumes RUC-annotated members (ISerializer, JasperFx.Events aggregator graph, CloseAndBuildAs / GenericFactoryCache fallbacks, FastExpressionCompiler). Document/event/projection types flow in from StoreOptions / Schema.For<T>() / projection registration and are preserved per the AOT publishing guide; AOT consumers supply a source-generator-backed serializer + pre-generated codegen artifacts.")]
[UnconditionalSuppressMessage("Trimming", "IL2090",
    Justification = "Class-level: generic class type-argument flow on the aggregator / storage instantiation. Types preserved at the projection-registration boundary.")]
[UnconditionalSuppressMessage("AOT", "IL3050",
    Justification = "Class-level: uses Type.MakeGenericType / MethodInfo.MakeGenericMethod / Activator.CreateInstance / FastExpressionCompiler — runtime code generation. AOT consumers pre-generate codegen artifacts (codegen write) and supply source-generator-backed serializer impls per the AOT publishing guide.")]
internal abstract class AdvancedSqlQueryHandlerBase<TResult>
{
    protected readonly char Placeholder;
    protected readonly object[] Parameters;
    protected readonly string Sql;
    protected List<ISelector> Selectors = new();

    protected AdvancedSqlQueryHandlerBase(char placeholder, string sql, object[] parameters)
    {
        Sql = sql.TrimStart();
        Placeholder = placeholder;
        Parameters = parameters;
    }

    public List<Type> DocumentTypes { get; } = new();

    public void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
    {
        var firstParameter = Parameters.FirstOrDefault();

        if (Parameters.Length == 1 && firstParameter != null && firstParameter.IsAnonymousType())
        {
            NamedParameterHelper.AppendSqlWithNamedParameters(builder, Sql, firstParameter);
        }
        else
        {
            var cmdParameters = builder.AppendWithParameters(Sql, Placeholder);
            if (cmdParameters.Length != Parameters.Length)
            {
                throw new InvalidOperationException("Wrong number of supplied parameters");
            }

            for (var i = 0; i < cmdParameters.Length; i++)
            {
                if (Parameters[i] == null)
                {
                    cmdParameters[i].Value = DBNull.Value;
                }
                else
                {
                    cmdParameters[i].Value = Parameters[i];
                    cmdParameters[i].NpgsqlDbType =
                        PostgresqlProvider.Instance.ToParameterType(Parameters[i].GetType());
                }
            }
        }
    }

    protected async Task<T?> ReadNestedRowAsync<T>(DbDataReader reader, int rowIndex, CancellationToken token) where T : notnull
    {
        var innerReader = reader.GetData(rowIndex) ??
                          throw new ArgumentException("Invalid row index", nameof(rowIndex));

        await using (innerReader.ConfigureAwait(false))
        {
            if (await innerReader.ReadAsync(token).ConfigureAwait(false))
            {
                return await ((ISelector<T>)Selectors[rowIndex]).ResolveAsync(innerReader, token).ConfigureAwait(false);
            }
        }
        return default;
    }

    protected ISelectClause GetSelectClause<T>(IStorageSession session) where T : notnull
    {
        if (typeof(T) == typeof(string))
        {
            return new ScalarStringSelectClause(string.Empty, string.Empty);
        }

        if (PostgresqlProvider.Instance.HasTypeMapping(typeof(T)))
        {
            return typeof(ScalarSelectClause<>).CloseAndBuildAs<ISelectClause>(string.Empty, string.Empty, typeof(T));
        }

        if (typeof(T).GetProperty("Id") == null && typeof(T).GetProperty("id") == null)
        {
            return new DataSelectClause<T>(string.Empty, string.Empty);
        }
        return session.StorageFor<T>();
    }

    protected void RegisterResultType<T>(IStorageSession session) where T : notnull
    {
        var selectClause = GetSelectClause<T>(session);
        Selectors.Add(selectClause.BuildSelector(session));
        if (selectClause is IDocumentStorage)
        {
            DocumentTypes.Add(typeof(T));
        }
    }

    public Task<int> StreamJson(Stream stream, DbDataReader reader, CancellationToken token)
    {
        throw new NotImplementedException();
    }

    public async Task<IReadOnlyList<TResult>> HandleAsync(DbDataReader reader, IStorageSession session,
        CancellationToken token)
    {
        var list = new List<TResult>();
        await foreach (var result in EnumerateResults(reader, token).ConfigureAwait(false))
        {
            list.Add(result);
        }

        return list;
    }

    public abstract IAsyncEnumerable<TResult> EnumerateResults(DbDataReader reader, CancellationToken token);
}
