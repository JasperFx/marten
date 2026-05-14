#nullable enable
using System;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using Marten.Internal;
using Marten.Internal.Storage;
using Marten.Linq.Selectors;
using Marten.Services;
using Npgsql;
using Weasel.Postgresql;
using System.Diagnostics.CodeAnalysis;

namespace Marten.Linq.QueryHandlers;

[UnconditionalSuppressMessage("Trimming", "IL2026",
    Justification = "Class-level: consumes RUC-annotated members (ISerializer, JasperFx.Events aggregator graph, CloseAndBuildAs / GenericFactoryCache fallbacks, FastExpressionCompiler). Document/event/projection types flow in from StoreOptions / Schema.For<T>() / projection registration and are preserved per the AOT publishing guide; AOT consumers supply a source-generator-backed serializer + pre-generated codegen artifacts.")]
[UnconditionalSuppressMessage("Trimming", "IL2087",
    Justification = "Class-level: generic method/type argument flows reflective Type values into a DAM-annotated target. Source preserved at the registration boundary.")]
[UnconditionalSuppressMessage("AOT", "IL3050",
    Justification = "Class-level: uses Type.MakeGenericType / MethodInfo.MakeGenericMethod / Activator.CreateInstance / FastExpressionCompiler — runtime code generation. AOT consumers pre-generate codegen artifacts (codegen write) and supply source-generator-backed serializer impls per the AOT publishing guide.")]
internal class LoadByIdHandler<T, TId>: IQueryHandler<T> where T : notnull where TId : notnull
{
    private readonly TId _id;
    private readonly IDocumentStorage<T> storage;
    private static readonly Type[] _identityTypes = [typeof(int), typeof(long), typeof(string), typeof(Guid)];

    public LoadByIdHandler(IDocumentStorage<T, TId> documentStorage, TId id)
    {
        storage = documentStorage;

        _id = id;
    }

    public void ConfigureCommand(ICommandBuilder sql, IMartenSession session)
    {
        sql.Append("select ");

        var fields = storage.SelectFields();
        sql.Append(fields[0]);
        for (var i = 1; i < fields.Length; i++)
        {
            sql.Append(", ");
            sql.Append(fields[i]);
        }

        sql.Append(" from ");
        sql.Append(storage.FromObject);
        sql.Append(" as d where id = ");

        if (_identityTypes.Contains(typeof(TId)))
        {
            sql.AppendParameter(_id);
        }
        else
        {
            var valueType = ValueTypeInfo.ForType(typeof(TId));
            typeof(Appender<,>).CloseAndBuildAs<IAppender<TId>>(valueType, typeof(TId), valueType.SimpleType)
                .Append(sql, _id);
        }


        storage.AddTenancyFilter(sql, session.TenantId);
    }




    public T Handle(DbDataReader reader, IMartenSession session)
    {
        var selector = (ISelector<T>)storage.BuildSelector(session);
        return reader.Read() ? selector.Resolve(reader) : default;
    }

    public async Task<T> HandleAsync(DbDataReader reader, IMartenSession session, CancellationToken token)
    {
        var selector = (ISelector<T>)storage.BuildSelector(session);
        if (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            return await selector.ResolveAsync(reader, token).ConfigureAwait(false);
        }

        return default;
    }

    public Task<int> StreamJson(Stream stream, DbDataReader reader, CancellationToken token)
    {
        return reader.As<NpgsqlDataReader>().StreamOne(stream, token);
    }
}

internal interface IAppender<TId>
{
    public void Append(ICommandBuilder builder, TId id);
}

[UnconditionalSuppressMessage("Trimming", "IL2026",
    Justification = "Class-level: consumes RUC-annotated members (ISerializer, JasperFx.Events aggregator graph, CloseAndBuildAs / GenericFactoryCache fallbacks, FastExpressionCompiler). Document/event/projection types flow in from StoreOptions / Schema.For<T>() / projection registration and are preserved per the AOT publishing guide; AOT consumers supply a source-generator-backed serializer + pre-generated codegen artifacts.")]
internal class Appender<TId, TSimple>: IAppender<TId>
{
    private readonly ValueTypeInfo _valueType;

    public Appender(ValueTypeInfo valueType)
    {
        _valueType = valueType;
    }

    public void Append(ICommandBuilder builder, TId id)
    {
        var simple = _valueType.UnWrapper<TId, TSimple>()(id);
        builder.AppendParameter(simple);
    }
}
