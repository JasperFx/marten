#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events;
using Marten.Events.Operations;
using Marten.Internal.Sessions;
using Marten.Testing.Harness;
using Npgsql;
using NpgsqlTypes;
using Shouldly;
using TenantPartitionedEventsTests.Fixtures;
using Weasel.Postgresql;
using Xunit;

namespace TenantPartitionedEventsTests.Regressions;

/// <summary>
/// #5062 — <c>mt_quick_append_events</c> called with an EMPTY event array used to return
/// <c>{NULL}</c>, because <c>array_length('{}', 1)</c> is NULL in PostgreSQL (not 0), so
/// <c>event_version + array_length(event_ids, 1)</c> is NULL. Npgsql then failed to read that
/// <c>bigint[]</c> into <c>long[]</c> with
/// <c>InvalidCastException: Cannot read a non-nullable collection of elements because the
/// returned array contains nulls</c>.
///
/// <para>
/// The cast failure surfaced from <see cref="QuickAppendEventsOperationBase.PostprocessAsync"/>,
/// i.e. from inside the batch's callback loop — so it propagated out of the loop and discarded
/// whatever exception had already been collected for the batch. Callers saw an unrelated,
/// non-retryable <c>InvalidCastException</c> instead of the real error.
/// </para>
/// </summary>
[Collection("guid-partitioned")]
public class Bug_5062_empty_quick_append
{
    private readonly GuidPartitionedFixture _fixture;

    public Bug_5062_empty_quick_append(GuidPartitionedFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// The issue's SQL repro. The parameter list of <c>mt_quick_append_events</c> varies with
    /// configuration (metadata columns, server timestamps, tag tables, bigint events), so read
    /// the deployed signature back out of the catalog and feed every array parameter an empty
    /// array — that way this pins the function rather than one config's hand-written call.
    /// </summary>
    [Fact]
    public async Task function_returns_a_non_null_version_for_an_empty_event_array()
    {
        var tenant = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, tenant);

        var streamId = await _fixture.AppendNEventsAsync(tenant, 3);

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        var arguments = await readFunctionArgumentsAsync(conn);
        var sql = $"select {_fixture.SchemaName}.mt_quick_append_events({buildEmptyArgumentList(arguments)})";

        await using var cmd = conn.CreateCommand(sql)
            .With("stream", streamId, NpgsqlDbType.Uuid)
            .With("tenant", tenant, NpgsqlDbType.Varchar);

        var raw = await cmd.ExecuteScalarAsync();

        // int[] when EnableBigIntEvents is off, bigint[] when it is on. Before the fix this came
        // back as a single-element array holding NULL.
        var values = raw switch
        {
            int[] ints => Array.ConvertAll(ints, x => (long)x),
            long[] longs => longs,
            _ => throw new InvalidOperationException($"Unexpected return type {raw?.GetType()?.FullName}")
        };

        values.Length.ShouldBe(1);

        // Zero events appended -> the stream's version is unchanged.
        values[0].ShouldBe(3);
    }

    /// <summary>
    /// Same empty append, but driven through Marten's own generated call site + result read, which
    /// is where the <c>InvalidCastException</c> actually landed on the reporter's system.
    /// </summary>
    [Fact]
    public async Task empty_quick_append_operation_is_a_clean_no_op()
    {
        var tenant = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, tenant);

        var streamId = await _fixture.AppendNEventsAsync(tenant, 2);
        var eventCountBefore = await _fixture.CountEventsForTenantAsync(tenant, _fixture.SchemaName);

        await using var session = (DocumentSessionBase)_fixture.Store.LightweightSession(tenant);

        var stream = StreamAction.Append(streamId, Array.Empty<IEvent>());
        stream.TenantId = tenant;

        var op = (QuickAppendEventsOperationBase)session.EventStorage().QuickAppendEvents(stream);
        op.Events = _fixture.Store.Options.EventGraph;
        session.QueueOperation(op);

        await Should.NotThrowAsync(() => session.SaveChangesAsync());

        (await _fixture.CountEventsForTenantAsync(tenant, _fixture.SchemaName)).ShouldBe(eventCountBefore);

        await using var query = _fixture.Store.QuerySession(tenant);
        var state = await query.Events.FetchStreamStateAsync(streamId);
        state.ShouldNotBeNull();
        state.Version.ShouldBe(2);
    }

    private async Task<IReadOnlyList<string>> readFunctionArgumentsAsync(NpgsqlConnection conn)
    {
        var raw = (string?)await conn.CreateCommand(@"
select pg_get_function_arguments(p.oid)
from pg_proc p
join pg_namespace n on n.oid = p.pronamespace
where n.nspname = :schema and p.proname = 'mt_quick_append_events'")
            .With("schema", _fixture.SchemaName, NpgsqlDbType.Varchar)
            .ExecuteScalarAsync();

        raw.ShouldNotBeNull();

        // No PostgreSQL type rendered in this signature contains a comma ("character varying[]",
        // "timestamp with time zone[]", "integer DEFAULT NULL::integer"), so a flat split is safe.
        return raw!.Split(", ", StringSplitOptions.RemoveEmptyEntries).ToArray();
    }

    private static string buildEmptyArgumentList(IReadOnlyList<string> arguments)
    {
        var rendered = new List<string>(arguments.Count);

        for (var i = 0; i < arguments.Count; i++)
        {
            // "<name> <type>" or "<name> <type> DEFAULT <expression>"
            var declaration = arguments[i];
            var defaultAt = declaration.IndexOf(" DEFAULT ", StringComparison.Ordinal);
            if (defaultAt > -1)
            {
                declaration = declaration.Substring(0, defaultAt);
            }

            var type = declaration.Substring(declaration.IndexOf(' ') + 1);

            rendered.Add(i switch
            {
                0 => ":stream",
                1 => "'Trip'",
                2 => ":tenant",
                // Every remaining event column is an array; the lone scalar is the trailing
                // expected_version, which NULL turns into "no optimistic concurrency check".
                _ => type.EndsWith("[]", StringComparison.Ordinal) ? $"'{{}}'::{type}" : $"NULL::{type}"
            });
        }

        return rendered.Join(", ");
    }
}
