#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Events.Operations;
using Marten.Events.Schema;
using Marten.Storage;
using Weasel.Postgresql;

namespace Marten.Internal.Sessions;

public abstract partial class DocumentSessionBase
{
    // The cold versions this session has observed, by tenant and stream id; 0 means "no cold part".
    // Every appended stream in here is verified when the unit of work commits.
    private Dictionary<(string TenantId, string StreamKey), long>? _coldVersions;

    internal void RememberColdVersion(string tenantId, string streamKey, long coldVersion)
    {
        _coldVersions ??= new Dictionary<(string, string), long>();
        _coldVersions[(tenantId, streamKey)] = coldVersion;
    }

    internal void ForgetColdVersion(string tenantId, string streamKey)
    {
        _coldVersions?.Remove((tenantId, streamKey));
    }

    internal async ValueTask<long?> ColdVersionOfAsync(StreamAction stream, CancellationToken token)
    {
        if (stream.ActionType == StreamActionType.Start) return null;

        var key = coldKeyOf(stream);
        if (_coldVersions == null || !_coldVersions.ContainsKey(key))
        {
            await resolveColdVersionsAsync(token).ConfigureAwait(false);
        }

        var coldVersion = _coldVersions![key];
        return coldVersion > 0 ? coldVersion : null;
    }

    private (string TenantId, string StreamKey) coldKeyOf(StreamAction stream) => (stream.TenantId ?? TenantId, stream.Key!);

    private IEnumerable<StreamAction> appendedStreams() =>
        _workTracker.Streams.Where(x => x.ActionType == StreamActionType.Append);

    /// <summary>
    ///     One query for every appended stream whose cold version this session has not observed yet.
    /// </summary>
    private async Task resolveColdVersionsAsync(CancellationToken token)
    {
        var events = Options.EventGraph;

        var unknown = appendedStreams()
            .Select(coldKeyOf)
            .Where(x => _coldVersions == null || !_coldVersions.ContainsKey(x))
            .Distinct()
            .GroupBy(x => x.TenantId)
            .ToArray();

        _coldVersions ??= new Dictionary<(string, string), long>();
        if (unknown.Length == 0) return;

        var builder = new BatchBuilder { TenantId = TenantId };
        for (var i = 0; i < unknown.Length; i++)
        {
            if (i > 0) builder.StartNewCommand();

            var tenant = unknown[i];
            builder.Append($"select id, coalesce({StreamsTable.ColdVersionColumn}, 0) from {events.DatabaseSchemaName}.{StreamsTable.TableName} where id = any(");
            builder.AppendParameter(tenant.Select(x => x.StreamKey).ToArray());

            builder.Append(')');

            if (events.TenancyStyle == TenancyStyle.Conjoined)
            {
                builder.Append(" and tenant_id = ");
                builder.AppendParameter(tenant.Key);
            }
        }

        // A stream without a row yet is created by this append, so it has no cold part.
        foreach (var tenant in unknown)
        {
            foreach (var stream in tenant) _coldVersions[stream] = 0;
        }

        await using var reader = await ExecuteReaderAsync(builder.Compile(), token).ConfigureAwait(false);
        foreach (var tenant in unknown)
        {
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                var streamKey = await reader.GetFieldValueAsync<string>(0, token).ConfigureAwait(false);
                _coldVersions[(tenant.Key, streamKey)] = await reader.GetFieldValueAsync<long>(1, token).ConfigureAwait(false);
            }

            await reader.NextResultAsync(token).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Queued after the listeners ran, so it lands behind the appends: their <c>mt_streams</c> updates hold
    ///     the row locks, which makes the check exact. Only streams whose cold version was observed are checked.
    /// </summary>
    private void queueColdVersionVerification()
    {
        if (_coldVersions == null || _coldVersions.Count == 0) return;

        var observed = appendedStreams()
            .Where(x => x.Events.Count > 0)
            .Select(coldKeyOf)
            .Distinct()
            .Where(_coldVersions.ContainsKey)
            .GroupBy(x => x.TenantId);

        foreach (var tenant in observed)
        {
            QueueOperation(new AssertColdVersionsOperation(Options.EventGraph, tenant.Key,
                tenant.Select(x => (x.StreamKey, _coldVersions[x])).ToArray()));
        }
    }

    private void forgetObservedColdVersions()
    {
        _coldVersions?.Clear();
    }
}
