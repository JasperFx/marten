#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Schema;
using NpgsqlTypes;
using Weasel.Core;
using Weasel.Postgresql;

namespace Marten.Storage.Identification.ClosedShape;

/// <summary>
/// W3 spike: hand-written upsert operation for documents with the minimal
/// table shape <c>(id, data)</c> — no metadata columns. Emits plain
/// <c>INSERT … ON CONFLICT (id) DO UPDATE SET data = excluded.data</c>
/// instead of calling a per-document <c>mt_upsert_*</c> PostgreSQL function.
/// The codegen-emitted operation classes the closed-shape hierarchy
/// replaces ultimately drive the same kind of parameter binding; this
/// spike proves the pattern works end-to-end without Roslyn JIT.
/// </summary>
/// <remarks>
/// Scoped tightly for the spike — requires
/// <c>mapping.Metadata.DisableInformationalFields()</c> at registration
/// time so the actual table doesn't carry <c>mt_version</c> /
/// <c>mt_dotnet_type</c> / <c>mt_last_modified</c> columns. Adding those
/// is mechanical; left out so the spike's diff stays minimal.
/// </remarks>
internal sealed class ClosedShapeUpsertOperation<TDoc>: IDocumentStorageOperation
    where TDoc : notnull
{
    private readonly TDoc _document;
    private readonly Guid _id;
    private readonly string _sqlPrefix;
    private readonly string _sqlSuffix;
    private readonly OperationRole _role;

    public ClosedShapeUpsertOperation(TDoc document, Guid id, string sqlPrefix, string sqlSuffix, OperationRole role)
    {
        _document = document;
        _id = id;
        _sqlPrefix = sqlPrefix;
        _sqlSuffix = sqlSuffix;
        _role = role;
    }

    public Type DocumentType => typeof(TDoc);

    public object Document => _document;

    public Marten.Internal.DirtyTracking.IChangeTracker ToTracker(IMartenSession session)
        => new Marten.Internal.DirtyTracking.ChangeTracker<TDoc>(session, _document);

    public OperationRole Role() => _role;

    public void ConfigureCommand(ICommandBuilder builder, IMartenSession session)
    {
        // Closed-shape ConfigureCommand: prefix + 2 grouped params + suffix.
        // Mirrors what codegen emits but as a constant-shape method body —
        // no per-call branching on AppendMode / metadata-column flags.
        builder.Append(_sqlPrefix);

        var pb = builder.CreateGroupedParameterBuilder(',');
        var idParam = pb.AppendParameter(_id);
        idParam.NpgsqlDbType = NpgsqlDbType.Uuid;

        var dataParam = pb.AppendParameter<object>(DBNull.Value);
        session.Serializer.WriteToParameter(dataParam, _document);

        builder.Append(_sqlSuffix);
    }

    public void Postprocess(DbDataReader reader, IList<Exception> exceptions)
    {
        // No metadata writeback (no Version / Revision columns).
    }

    public Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
        => Task.CompletedTask;
}
