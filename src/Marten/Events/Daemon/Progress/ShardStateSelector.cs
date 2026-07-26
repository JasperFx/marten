using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events.Daemon;
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

            // #5048 / jasperfx#565: rehydrate the classified failure so a consumer polling the database
            // (CritterWatch when the publishing node is down) gets the same shape as a live ShardState
            // observer, rather than only being able to see that the shard is Paused.
            string? category = null;
            if (!await reader.IsDBNullAsync(nextIndex, token).ConfigureAwait(false))
            {
                category = await reader.GetFieldValueAsync<string>(nextIndex, token).ConfigureAwait(false);
            }
            nextIndex++;

            long? failureSequence = null;
            if (!await reader.IsDBNullAsync(nextIndex, token).ConfigureAwait(false))
            {
                failureSequence = await reader.GetFieldValueAsync<long>(nextIndex, token).ConfigureAwait(false);
            }
            nextIndex++;

            string? failureEventType = null;
            if (!await reader.IsDBNullAsync(nextIndex, token).ConfigureAwait(false))
            {
                failureEventType = await reader.GetFieldValueAsync<string>(nextIndex, token).ConfigureAwait(false);
            }
            nextIndex++;

            string? failureTenantId = null;
            if (!await reader.IsDBNullAsync(nextIndex, token).ConfigureAwait(false))
            {
                failureTenantId = await reader.GetFieldValueAsync<string>(nextIndex, token).ConfigureAwait(false);
            }
            nextIndex++;

            state.Failure = buildFailure(category, failureSequence, failureEventType, failureTenantId, state);
        }

        return state;
    }

    /// <summary>
    /// #5048 / jasperfx#565: the persisted row is a lossy projection of <see cref="ShardFailure" /> — by
    /// design, since the columns exist to answer "why is this shard down" and not to reconstitute an
    /// exception. <c>failure_category</c> is the presence flag: no category means no failure to report.
    /// </summary>
    private static ShardFailure? buildFailure(string? category, long? sequence, string? eventTypeName,
        string? tenantId, ShardState state)
    {
        if (category.IsEmpty() || !Enum.TryParse<ShardFailureCategory>(category, out var parsed))
        {
            return null;
        }

        // ShardFailure.Detail is exactly what PauseReason has always carried, which is why the text needed
        // no column of its own. Message is the same text here: the short form is a property of the live
        // Exception, and that is not persisted.
        var detail = state.PauseReason ?? string.Empty;

        return new ShardFailure
        {
            Category = parsed,
            // Not persisted — the reason text in Detail is what an operator acts on, and inventing a type
            // name would be worse than admitting we don't have one.
            ExceptionType = UnknownExceptionType,
            RootExceptionType = UnknownExceptionType,
            Message = detail,
            Detail = detail,
            // The pause/stop publication stamps LastHeartbeat at the moment it classifies the failure, so
            // the persisted heartbeat is the closest thing the row has to the failure's timestamp.
            OccurredAt = state.LastHeartbeat ?? default,
            Event = sequence.HasValue
                ? new EventFailureDetails
                {
                    Sequence = sequence.Value, EventTypeName = eventTypeName, TenantId = tenantId
                }
                : null
        };
    }

    /// <summary>
    /// Placeholder for <see cref="ShardFailure.ExceptionType" /> / <see cref="ShardFailure.RootExceptionType" />
    /// on a failure rehydrated from the database, where the exception types were never persisted. Both members
    /// are <c>required</c> on the record, so a sentinel is unavoidable; a distinctive one beats an empty string
    /// that reads like a real (blank) type name.
    /// </summary>
    internal const string UnknownExceptionType = "Unknown";
}
