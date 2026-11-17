#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Daemon.HighWater;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using TenantPartitionedEventsTests.Fixtures;
using Weasel.Postgresql;
using Xunit;

namespace TenantPartitionedEventsTests.Daemon;

/// <summary>
///     #5738 / jasperfx#1031 under per-tenant partitioned events, where the database keeps one high water mark
///     per tenant (<c>HighWaterMark:{tenant}</c>) and the store-global row means nothing.
/// </summary>
[Collection("guid-partitioned")]
public class high_water_advance_contract_tests
{
    private readonly GuidPartitionedFixture _fixture;

    public high_water_advance_contract_tests(GuidPartitionedFixture fixture)
    {
        _fixture = fixture;
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private IEventStore eventStore => _fixture.Store;

    private async Task<IEventDatabase> databaseAsync() => (await eventStore.AllDatabases()).Single();

    /// <summary>
    ///     A tenant with three events whose middle one is removed, so its own sequence has one number with no
    ///     committed event below its ceiling.
    /// </summary>
    private async Task<string> tenantWithAHoleAsync()
    {
        var tenant = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(Token, tenant);
        await _fixture.AppendNEventsAsync(tenant, 3);
        await executeAsync(
            $"delete from {_fixture.SchemaName}.mt_events where tenant_id = '{tenant}' and seq_id = 2");
        return tenant;
    }

    [Fact]
    public async Task a_named_tenant_previews_and_advances_its_own_mark_only()
    {
        var red = await tenantWithAHoleAsync();
        var blue = await tenantWithAHoleAsync();
        var database = await databaseAsync();

        var preview = (await eventStore.PreviewHighWaterAdvanceAsync(database, red, Token)).Single();
        preview.TenantId.ShouldBe(red);
        preview.CurrentMark.ShouldBe(0);
        preview.CommittedCeiling.ShouldBe(3);
        preview.MissingSequenceCount.ShouldBe(1);

        var advance = (await eventStore.AdvanceHighWaterMarkToLatestAsync(database, red, preview.CurrentMark, Token))
            .Single();
        advance.TenantId.ShouldBe(red);
        advance.Outcome.ShouldBe(HighWaterAdvanceOutcome.Advanced);
        advance.PreviousMark.ShouldBe(0);
        advance.NewMark.ShouldBe(3);
        advance.SkippedSequenceCount.ShouldBe(1);

        (await readMarkAsync(red)).ShouldBe(3);
        (await readMarkAsync(blue)).ShouldBeNull();
    }

    [Fact]
    public async Task a_null_tenant_previews_and_advances_every_tenants_own_mark()
    {
        var red = await tenantWithAHoleAsync();
        var blue = await tenantWithAHoleAsync();
        var database = await databaseAsync();

        var previews = await eventStore.PreviewHighWaterAdvanceAsync(database, null, Token);
        previews.ShouldNotContain(x => x.TenantId == null);
        previews.Single(x => x.TenantId == red).CommittedCeiling.ShouldBe(3);
        previews.Single(x => x.TenantId == blue).CommittedCeiling.ShouldBe(3);

        var advances = await eventStore.AdvanceHighWaterMarkToLatestAsync(database, null, null, Token);
        advances.ShouldNotContain(x => x.TenantId == null);
        advances.Single(x => x.TenantId == red).Outcome.ShouldBe(HighWaterAdvanceOutcome.Advanced);
        advances.Single(x => x.TenantId == blue).Outcome.ShouldBe(HighWaterAdvanceOutcome.Advanced);

        (await readMarkAsync(red)).ShouldBe(3);
        (await readMarkAsync(blue)).ShouldBe(3);
    }

    [Fact]
    public async Task an_expectation_needs_a_named_tenant_and_then_guards_only_that_one()
    {
        var red = await tenantWithAHoleAsync();
        var database = await databaseAsync();

        await Should.ThrowAsync<ArgumentException>(
            () => eventStore.AdvanceHighWaterMarkToLatestAsync(database, null, 0, Token));

        var stale = (await eventStore.AdvanceHighWaterMarkToLatestAsync(database, red, 2, Token)).Single();
        stale.Outcome.ShouldBe(HighWaterAdvanceOutcome.RefusedStaleExpectation);
        stale.PreviousMark.ShouldBe(0);
        (await readMarkAsync(red)).ShouldBeNull();

        var advance = (await eventStore.AdvanceHighWaterMarkToLatestAsync(database, red, 0, Token)).Single();
        advance.Outcome.ShouldBe(HighWaterAdvanceOutcome.Advanced);
        (await readMarkAsync(red)).ShouldBe(3);
    }

    [Fact]
    public async Task the_advanced_overload_advances_the_tenants_own_mark()
    {
        var red = await tenantWithAHoleAsync();

        await _fixture.Store.Advanced.AdvanceHighWaterMarkToLatestAsync(red, Token);

        (await readMarkAsync(red)).ShouldBe(3);
    }

    private async Task<long?> readMarkAsync(string tenant)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync(Token);
        await using var cmd = conn.CreateCommand(
            $"select last_seq_id from {_fixture.SchemaName}.mt_event_progression where name = :name");
        cmd.Parameters.AddWithValue("name", "HighWaterMark:" + tenant);
        return await cmd.ExecuteScalarAsync(Token) as long?;
    }

    private static async Task executeAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync(Token);
        await conn.CreateCommand(sql).ExecuteNonQueryAsync(Token);
    }
}
