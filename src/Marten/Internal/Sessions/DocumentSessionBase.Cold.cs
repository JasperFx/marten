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
    private Dictionary<(string TenantId, string StreamKey), long>? _observedColdVersions;
    private bool _queuedColdVersionVerification;

    /// <summary>
    ///     The cold versions this unit of work has observed, by tenant and stream id; 0 means "no cold part".
    ///     Every appended stream in here is verified when the unit of work commits.
    /// </summary>
    internal virtual Dictionary<(string TenantId, string StreamKey), long> ObservedColdVersions =>
        _observedColdVersions ??= new Dictionary<(string, string), long>();

    internal void RememberColdVersion(string tenantId, string streamKey, long coldVersion)
    {
        ObservedColdVersions[(tenantId, streamKey)] = coldVersion;
    }

    internal void ForgetColdVersion(string tenantId, string streamKey)
    {
        ObservedColdVersions.Remove((tenantId, streamKey));
    }

    internal async ValueTask<long?> ColdVersionOfAsync(StreamAction stream, CancellationToken token)
    {
        if (stream.ActionType == StreamActionType.Start) return null;

        return await ColdVersionOfAsync(coldKeyOf(stream), token).ConfigureAwait(false);
    }

    internal async ValueTask<long?> ColdVersionOfAsync((string TenantId, string StreamKey) key, CancellationToken token)
    {
        var observed = ObservedColdVersions;
        if (!observed.TryGetValue(key, out var coldVersion))
        {
            // The stream does not have to be one this unit of work appends to
            await resolveColdVersionsAsync(key, token).ConfigureAwait(false);
            coldVersion = observed[key];
        }

        return coldVersion > 0 ? coldVersion : null;
    }

    private (string TenantId, string StreamKey) coldKeyOf(StreamAction stream) => (stream.TenantId ?? TenantId, stream.Key!);

    private IEnumerable<StreamAction> appendedStreams() =>
        _workTracker.Streams.Where(x => x.ActionType == StreamActionType.Append);

    /// <summary>
    ///     One query for <paramref name="asked" /> and every appended stream whose cold version this unit of work
    ///     has not observed yet.
    /// </summary>
    private async Task resolveColdVersionsAsync((string TenantId, string StreamKey) asked, CancellationToken token)
    {
        var events = Options.EventGraph;
        var observed = ObservedColdVersions;

        var unknown = appendedStreams()
            .Select(coldKeyOf)
            .Append(asked)
            .Where(x => !observed.ContainsKey(x))
            .Distinct()
            .GroupBy(x => x.TenantId)
            .ToArray();

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
            foreach (var stream in tenant) observed[stream] = 0;
        }

        await using var reader = await ExecuteReaderAsync(builder.Compile(), token).ConfigureAwait(false);
        foreach (var tenant in unknown)
        {
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                var streamKey = await reader.GetFieldValueAsync<string>(0, token).ConfigureAwait(false);
                observed[(tenant.Key, streamKey)] = await reader.GetFieldValueAsync<long>(1, token).ConfigureAwait(false);
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
        // A commit retried after a failure verifies once, with what is observed now
        if (_queuedColdVersionVerification)
        {
            _workTracker.RemoveOperationsOfType<AssertColdVersionsOperation>();
            _queuedColdVersionVerification = false;
        }

        if (_observedColdVersions is not { Count: > 0 } observed) return;

        var verified = appendedStreams()
            .Where(x => x.Events.Count > 0)
            .Select(coldKeyOf)
            .Distinct()
            .Where(observed.ContainsKey)
            .GroupBy(x => x.TenantId);

        foreach (var tenant in verified)
        {
            QueueOperation(new AssertColdVersionsOperation(Options.EventGraph, tenant.Key,
                tenant.Select(x => (x.StreamKey, observed[x])).ToArray()));
            _queuedColdVersionVerification = true;
        }
    }

    private void forgetObservedColdVersions()
    {
        _observedColdVersions?.Clear();
        _queuedColdVersionVerification = false;
    }
}
