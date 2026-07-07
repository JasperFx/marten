#nullable enable
using JasperFx.Events;
using Marten.Events.Operations;
using Marten.Internal;
using Weasel.Core;
using Weasel.Storage;

namespace Marten.EventStorage.Rich;

/// <summary>
/// Closed-shape <see cref="AppendEventOperationBase"/> for the Rich
/// (full-mode) per-event append path. Binds one parameter per column in
/// the order the dialect's
/// <see cref="RichEventStorageDescriptor.AppendEventSqlPrefix"/> declares.
/// </summary>
/// <remarks>
/// <para>
/// W4 (#4413). The codegen path inlines per-column writes into a generated
/// concrete class per <c>(stream-identity × tenancy × DCB-storage-mode ×
/// metadata-flags)</c> tuple. This closed-shape equivalent uses a single
/// class driven by descriptor flags + a metadata-binder array — runtime
/// dispatch is one virtual call per metadata binder, plus the per-column
/// inlined writes. Source-gen (W5) takes this exact shape and emits a
/// concrete subclass per closed-shape configuration so the metadata binder
/// loop's overhead can be flattened into inlined writes.
/// </para>
/// <para>
/// Column order matches
/// <c>EventsTable.SelectColumns()</c> (data / type / mt_dotnet_type at
/// 0/1/2; followed by id, stream_id, version, timestamp, tenant_id, then
/// the metadata slice — see <see cref="PostgresEventStoreDialect.BuildAppendEventFullColumnsAndPrefix"/>).
/// </para>
/// </remarks>
internal sealed class RichAppendEventOperation: AppendEventOperationBase
{
    private readonly RichEventStorageDescriptor _descriptor;

    public RichAppendEventOperation(RichEventStorageDescriptor descriptor, StreamAction stream, IEvent e)
        : base(stream, e)
    {
        _descriptor = descriptor;
    }

    public override void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
    {
        builder.Append(_descriptor.AppendEventSqlPrefix);

        var dialect = _descriptor.Dialect;
        IGroupedParameterBuilder pb = builder.CreateGroupedParameterBuilder(',');

        // --- Core columns, in the dialect's column order ---
        // Must match BuildAppendEventFullColumnsAndPrefix:
        //   data, type, mt_dotnet_type, id, stream_id, version, timestamp, tenant_id,
        //   then metadata binders (e.g., seq_id) at the end.
        // Parameter provider types come from the dialect's SetParameterType so
        // the op carries no direct Npgsql reference.

        // data — jsonb. Closed-shape uses the descriptor's serializer
        // closure (returns string). Source-gen target (#4413 hot-path
        // hardening) can switch to Serializer.WriteToParameter to skip the
        // intermediate UTF-16 allocation, matching codegen.
        dialect.SetParameterType(pb.AppendParameter(_descriptor.SerializeEventData(Event)), StorageColumnType.Json);

        dialect.SetParameterType(pb.AppendParameter(Event.EventTypeName), StorageColumnType.String);
        dialect.SetParameterType(pb.AppendParameter(Event.DotNetTypeName), StorageColumnType.String);

        // #4515: bdata bytea (nullable). For JSON-serialized events this is
        // NULL; for binary-serialized events it carries the payload. Column
        // order matches EventsTable.SelectColumns — bdata is pinned at
        // position 3 right after mt_dotnet_type. The neutral AppendParameter
        // writes a null byte[] as DBNull.
        var bdataBytes = _descriptor.SerializeEventBdata(Event);
        dialect.SetParameterType(pb.AppendParameter(bdataBytes), StorageColumnType.Binary);

        dialect.SetParameterType(pb.AppendParameter(Event.Id), StorageColumnType.Guid);

        // stream_id — Guid streams use Stream.Id, string streams use Stream.Key.
        // The descriptor flag is set once at startup; per-call cost is a
        // branch-predictable field read.
        if (_descriptor.IsGuidStreamIdentity)
        {
            dialect.SetParameterType(pb.AppendParameter(Stream.Id), StorageColumnType.Guid);
        }
        else
        {
            dialect.SetParameterType(pb.AppendParameter(Stream.Key), StorageColumnType.String);
        }

        dialect.SetParameterType(pb.AppendParameter(Event.Version), StorageColumnType.Long);
        dialect.SetParameterType(pb.AppendParameter(Event.Timestamp), StorageColumnType.Timestamp);
        dialect.SetParameterType(pb.AppendParameter(Stream.TenantId), StorageColumnType.String);

        // --- Metadata binders (seq_id + any optional metadata columns) ---
        // Order matches the metadata slice of the SQL prefix's column list.
        // The dialect's descriptor builder owns both sides.
        var binders = _descriptor.MetadataBinders;
        for (var i = 0; i < binders.Length; i++)
        {
            binders[i].Bind(pb, Stream, Event, session);
        }

        builder.Append(_descriptor.AppendEventSqlSuffix);
    }
}
