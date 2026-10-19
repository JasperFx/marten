#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Events.Schema;
using Marten.Storage;
using Weasel.Postgresql;

namespace Marten.Internal.Sessions;

public abstract partial class DocumentSessionBase
{
    // tenant + stream id -> cold version as stored, 0 meaning "no cold part"
    private Dictionary<(string TenantId, object StreamId), long>? _coldVersions;

    internal void RememberColdVersion(string tenantId, object streamId, long coldVersion)
    {
        _coldVersions ??= new Dictionary<(string, object), long>();
        _coldVersions[(tenantId, streamId)] = coldVersion;
    }

    internal void ForgetColdVersion(string tenantId, object streamId)
    {
        _coldVersions?.Remove((tenantId, streamId));
    }

    internal long? ColdVersionOf(StreamAction stream)
    {
        if (stream.ActionType == StreamActionType.Start) return null;

        var key = (stream.TenantId ?? TenantId, streamIdOf(stream));
        if (_coldVersions == null || !_coldVersions.TryGetValue(key, out var coldVersion))
        {
            throw new InvalidOperationException(
                $"The cold version of stream '{key.Item2}' is not known yet. It is resolved for every appended stream during SaveChangesAsync, before IDocumentSessionListener.BeforeSaveChangesAsync, or when the stream is fetched with FetchForWriting or FetchLatest.");
        }

        return coldVersion > 0 ? coldVersion : null;
    }

    private object streamIdOf(StreamAction stream) =>
        Options.EventGraph.StreamIdentity == StreamIdentity.AsGuid ? stream.Id : stream.Key!;

    /// <summary>
    ///     Makes the cold version of every stream this unit of work appends to known before the listeners run:
    ///     one query for the streams that were not fetched through a live aggregation first.
    /// </summary>
    internal async Task ResolveColdVersionsAsync(CancellationToken token)
    {
        var events = Options.EventGraph;
        if (events.ColdEventStore == null) return;

        var unknown = _workTracker.Streams
            .Where(x => x.ActionType == StreamActionType.Append)
            .Select(x => (TenantId: x.TenantId ?? TenantId, StreamId: streamIdOf(x)))
            .Where(x => _coldVersions == null || !_coldVersions.ContainsKey(x))
            .Distinct()
            .GroupBy(x => x.TenantId)
            .ToArray();

        if (unknown.Length == 0) return;

        var builder = new BatchBuilder { TenantId = TenantId };
        for (var i = 0; i < unknown.Length; i++)
        {
            var tenant = unknown[i];
            if (i > 0) builder.StartNewCommand();

            builder.Append($"select id, coalesce({StreamsTable.ColdVersionColumn}, 0) from {events.DatabaseSchemaName}.{StreamsTable.TableName} where id = any(");
            if (events.StreamIdentity == StreamIdentity.AsGuid)
            {
                builder.AppendParameter(tenant.Select(x => (Guid)x.StreamId).ToArray());
            }
            else
            {
                builder.AppendParameter(tenant.Select(x => (string)x.StreamId).ToArray());
            }

            builder.Append(')');

            if (events.TenancyStyle == TenancyStyle.Conjoined)
            {
                builder.Append(" and tenant_id = ");
                builder.AppendParameter(tenant.Key);
            }
        }

        _coldVersions ??= new Dictionary<(string, object), long>();
        foreach (var tenant in unknown)
        {
            // A stream without a row yet is created by this append, so it has no cold part.
            foreach (var stream in tenant) _coldVersions[stream] = 0;
        }

        await using var reader = await ExecuteReaderAsync(builder.Compile(), token).ConfigureAwait(false);
        foreach (var tenant in unknown)
        {
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                object streamId = events.StreamIdentity == StreamIdentity.AsGuid
                    ? await reader.GetFieldValueAsync<Guid>(0, token).ConfigureAwait(false)
                    : await reader.GetFieldValueAsync<string>(0, token).ConfigureAwait(false);

                _coldVersions[(tenant.Key, streamId)] = await reader.GetFieldValueAsync<long>(1, token).ConfigureAwait(false);
            }

            await reader.NextResultAsync(token).ConfigureAwait(false);
        }
    }
}
