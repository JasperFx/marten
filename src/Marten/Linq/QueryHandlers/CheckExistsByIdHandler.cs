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
using Marten.Services;
using Weasel.Postgresql;
using System.Diagnostics.CodeAnalysis;

namespace Marten.Linq.QueryHandlers;

[UnconditionalSuppressMessage("Trimming", "IL2026",
    Justification = "Class-level: consumes RUC-annotated members (ISerializer, JasperFx.Events aggregator graph, CloseAndBuildAs / GenericFactoryCache fallbacks, FastExpressionCompiler). Document/event/projection types flow in from StoreOptions / Schema.For<T>() / projection registration and are preserved per the AOT publishing guide; AOT consumers supply a source-generator-backed serializer + pre-generated codegen artifacts.")]
[UnconditionalSuppressMessage("Trimming", "IL2087",
    Justification = "Class-level: generic method/type argument flows reflective Type values into a DAM-annotated target. Source preserved at the registration boundary.")]
[UnconditionalSuppressMessage("AOT", "IL3050",
    Justification = "Class-level: uses Type.MakeGenericType / MethodInfo.MakeGenericMethod / Activator.CreateInstance / FastExpressionCompiler — runtime code generation. AOT consumers pre-generate codegen artifacts (codegen write) and supply source-generator-backed serializer impls per the AOT publishing guide.")]
internal class CheckExistsByIdHandler<T, TId>: IQueryHandler<bool> where T : notnull where TId : notnull
{
    private readonly TId _id;
    private readonly IDocumentStorage<T> storage;
    private static readonly Type[] _identityTypes = [typeof(int), typeof(long), typeof(string), typeof(Guid)];

    public CheckExistsByIdHandler(IDocumentStorage<T, TId> documentStorage, TId id)
    {
        storage = documentStorage;
        _id = id;
    }

    public void ConfigureCommand(ICommandBuilder sql, IMartenSession session)
    {
        sql.Append("select exists(select 1 from ");
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
        sql.Append(")");
    }

    public bool Handle(DbDataReader reader, IMartenSession session)
    {
        return reader.Read() && reader.GetBoolean(0);
    }

    public async Task<bool> HandleAsync(DbDataReader reader, IMartenSession session, CancellationToken token)
    {
        if (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            return reader.GetBoolean(0);
        }

        return false;
    }

    public Task<int> StreamJson(Stream stream, DbDataReader reader, CancellationToken token)
    {
        throw new NotSupportedException("StreamJson is not supported for CheckExistsByIdHandler");
    }
}
