using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events.Projections;
using Marten.Linq.Selectors;

namespace Marten.Events.Daemon.Progress;

internal class ShardStateSelector: ISelector<ShardState>
{
    private readonly EventGraph _events;

    public ShardStateSelector(EventGraph events)
    {
        _events = events;
    }

    public ShardState Resolve(DbDataReader reader)
    {
        var name = reader.GetFieldValue<string>(0);
        var sequence = reader.GetFieldValue<long>(1);

        return new ShardState(name, sequence);
    }

    public async Task<ShardState> ResolveAsync(DbDataReader reader, CancellationToken token)
    {
        var name = await reader.GetFieldValueAsync<string>(0, token).ConfigureAwait(false);
        var sequence = await reader.GetFieldValueAsync<long>(1, token).ConfigureAwait(false);
        var state = new ShardState(name, sequence);

        var nextIndex = 2;

        if (_events.UseOptimizedProjectionRebuilds)
        {
            var modeString = await reader.GetFieldValueAsync<string>(nextIndex++, token).ConfigureAwait(false);
            if (Enum.TryParse<ShardMode>(modeString, out var mode))
            {
                state.Mode = mode;
            }

            state.RebuildThreshold = await reader.GetFieldValueAsync<long>(nextIndex++, token).ConfigureAwait(false);
            state.AssignedNodeNumber = await reader.GetFieldValueAsync<int>(nextIndex++, token).ConfigureAwait(false);
        }

        if (_events.EnableExtendedProgressionTracking)
        {
            if (!await reader.IsDBNullAsync(nextIndex, token).ConfigureAwait(false))
            {
                state.LastHeartbeat = await reader.GetFieldValueAsync<DateTimeOffset>(nextIndex, token).ConfigureAwait(false);
            }
            nextIndex++;

            if (!await reader.IsDBNullAsync(nextIndex, token).ConfigureAwait(false))
            {
                state.AgentStatus = await reader.GetFieldValueAsync<string>(nextIndex, token).ConfigureAwait(false);
            }
            nextIndex++;

            if (!await reader.IsDBNullAsync(nextIndex, token).ConfigureAwait(false))
            {
                state.PauseReason = await reader.GetFieldValueAsync<string>(nextIndex, token).ConfigureAwait(false);
            }
            nextIndex++;

            if (!await reader.IsDBNullAsync(nextIndex, token).ConfigureAwait(false))
            {
                state.RunningOnNode = await reader.GetFieldValueAsync<int>(nextIndex, token).ConfigureAwait(false);
            }
            nextIndex++;

            if (!await reader.IsDBNullAsync(nextIndex, token).ConfigureAwait(false))
            {
                state.WarningBehindThreshold = await reader.GetFieldValueAsync<long>(nextIndex, token).ConfigureAwait(false);
            }
            nextIndex++;

            if (!await reader.IsDBNullAsync(nextIndex, token).ConfigureAwait(false))
            {
                state.CriticalBehindThreshold = await reader.GetFieldValueAsync<long>(nextIndex, token).ConfigureAwait(false);
            }
            nextIndex++;
        }

        return state;
    }
}
