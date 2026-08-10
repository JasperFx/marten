#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using Marten;
using Marten.Events;
using Marten.Events.Schema;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace CoreTests.Partitioning;

public record SqliWidgetMade(string Name);

/// <summary>
/// Security regression: under <c>UseTenantPartitionedEvents</c> a tenant id was interpolated into a
/// <em>double-quoted</em> PostgreSQL identifier without doubling an embedded double quote, so a tenant id
/// containing one terminated the identifier early and the rest of the value executed as additional
/// statements (Npgsql allows multi-statement command text).
///
/// <para>
/// This is a different class from the two previously published advisories. Both of those were the
/// single-quoted <em>literal</em> class, fixed by <c>.Replace("'", "''")</c> or a bound parameter; neither
/// touches an identifier, and a search for the <c>'{</c> pattern does not surface these sites at all.
/// </para>
///
/// <para>
/// The reachable input is the tenant id, because sharded tenancy uses it as the partition suffix verbatim
/// (<c>ShardedTenancy.createPartitionsForTenant</c> builds <c>{ tenantId : tenantId }</c>) and validated
/// nothing — while the <c>DefaultTenancy</c> path has always run suffixes through
/// <c>AdvancedOperations.AssertValidPostgresqlIdentifiers</c>. That validator cannot simply be reused on the
/// sharded path: its <c>^[A-Za-z0-9_]+$</c> rule rejects a plain GUID, and normalizing the suffix would
/// produce a sequence the quick-append function could never find (see #4924). Hence escape at the sink plus
/// a narrow denylist guard, not an allowlist.
/// </para>
///
/// <para>
/// Note the payloads below contain <b>no spaces</b>, using <c>/**/</c> where whitespace is needed. Weasel's
/// <c>PostgresqlMigrator.AssertValidIdentifier</c> rejects an identifier containing a space but permits a
/// double quote, so a space-bearing payload is stopped on the schema-apply route by accident. A space-free
/// one is not — which is why <see cref="schema_apply_of_a_planted_suffix_does_not_execute_injected_sql"/>
/// matters and the schema-apply route is not, as it first appears, inherently safe.
/// </para>
/// </summary>
public class sql_injection_via_tenant_id_partition_suffix: IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private readonly string _schema = "sqli_seq_p" + Environment.ProcessId;
    private readonly string _partitionSchema = "sqli_seq_tenants_p" + Environment.ProcessId;

    /// <summary>
    /// Closes the quoted identifier, runs a CREATE TABLE, then comments out the statement's own tail.
    /// <c>/**/</c> stands in for every space so the value survives <c>AssertValidIdentifier</c>.
    ///
    /// <para>
    /// Kept short deliberately: once escaped, the whole payload becomes part of a single identifier, and
    /// PostgreSQL silently truncates identifiers at <c>NAMEDATALEN - 1</c> = 63 bytes. <c>mt_events_sequence_</c>
    /// is 19 of those, so a longer payload would still prove nothing executed but would leave a truncated
    /// sequence name to assert against. At this length the resulting name is exact.
    /// </para>
    /// </summary>
    private const string Payload = "s1\";CREATE/**/TABLE/**/sqli_mk(i/**/int);--";

    private const string MarkerTable = "sqli_mk";

    public sql_injection_via_tenant_id_partition_suffix(ITestOutputHelper output) => _output = output;

    public async ValueTask InitializeAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await DropMarkerAsync(conn);
        try { await conn.DropSchemaAsync(_partitionSchema); } catch { }
        try { await conn.DropSchemaAsync(_schema); } catch { }
    }

    public ValueTask DisposeAsync() => default;

    private DocumentStore BuildStore() => (DocumentStore)DocumentStore.For(opts =>
    {
        opts.Connection(ConnectionSource.ConnectionString);
        opts.DatabaseSchemaName = _schema;
        opts.DisableNpgsqlLogging = true;

        opts.Policies.AllDocumentsAreMultiTenanted();
        opts.Policies.PartitionMultiTenantedDocumentsUsingMartenManagement(_partitionSchema);

        opts.Events.TenancyStyle = TenancyStyle.Conjoined;
        opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
        opts.Events.UseTenantPartitionedEvents = true;
        opts.Events.AddEventType(typeof(SqliWidgetMade));

        opts.AutoCreateSchemaObjects = AutoCreate.All;
    });

    /// <summary>
    /// The reporter's exact route, one level below the public API: the imperative sequence provisioning that
    /// <c>ShardedTenancy.createPartitionsForTenant</c> and <c>AddMartenManagedTenantsAsync</c> both call. Before
    /// the fix this executed the payload's CREATE TABLE and returned normally, so the caller saw success.
    /// </summary>
    [Fact]
    public async Task ensure_sequences_does_not_execute_injected_sql()
    {
        await using var store = BuildStore();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        await PerTenantEventSequences.EnsureSequencesAsync(
            (PostgresqlDatabase)store.Storage.Database,
            _schema,
            new[] { Payload },
            CancellationToken.None);

        (await MarkerExistsAsync()).ShouldBeFalse(
            "the payload's CREATE TABLE must never execute — the identifier has to stay one identifier");

        // Positive half: the sequence is still created, under the FULL raw suffix. Anything less would mean
        // we had silently truncated at the quote, and quick-append's format('%I') lookup — which reads
        // partition_suffix straight from the tenants table — would then miss it.
        (await SequenceCountAsync($"mt_events_sequence_{Payload}")).ShouldBe(1L);

        // ...and specifically NOT the truncated name the injection would have produced.
        (await SequenceCountAsync("mt_events_sequence_s1")).ShouldBe(0L);
    }

    /// <summary>
    /// The persistent route. A poisoned suffix sits in the tenants table and every
    /// <c>ApplyAllConfiguredChangesToDatabaseAsync</c> re-emits its CREATE SEQUENCE, so the payload re-fires on
    /// each apply, for every store sharing the schema. Planting the row directly is exactly what sharded
    /// provisioning does — it writes the tenant id as the suffix verbatim.
    /// </summary>
    [Fact]
    public async Task schema_apply_of_a_planted_suffix_does_not_execute_injected_sql()
    {
        await using var store = BuildStore();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        await PlantRawSuffixAsync(Payload);

        await using var reopened = BuildStore();
        var partitions = reopened.Options.TenantPartitions!.Partitions;
        await partitions.InitializeAsync((PostgresqlDatabase)reopened.Storage.Database, CancellationToken.None);
        partitions.Partitions.ShouldContainKey(Payload);

        // Whether the apply succeeds or throws is not the assertion — a hostile suffix may well be rejected
        // somewhere downstream. The assertion is that nothing injected ever executes.
        try
        {
            await reopened.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        }
        catch (Exception)
        {
        }

        (await MarkerExistsAsync()).ShouldBeFalse(
            "a poisoned partition_suffix must not execute injected SQL on schema apply");
    }

    /// <summary>
    /// The drop side, which uses the same name-builder for DROP SEQUENCE. A store that already holds a
    /// poisoned suffix from an earlier version has to be able to clean it up without executing it — which is
    /// also why the new tenant-id guard is deliberately NOT applied to the removal path.
    /// </summary>
    [Fact]
    public async Task dropping_a_poisoned_sequence_does_not_execute_injected_sql()
    {
        await using var store = BuildStore();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        await PerTenantEventSequences.EnsureSequencesAsync(
            (PostgresqlDatabase)store.Storage.Database, _schema, new[] { Payload }, CancellationToken.None);
        await DropMarkerAsync();

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DROP SEQUENCE IF EXISTS {PerTenantEventSequences.QuotedSequenceName(_schema, Payload)}";
        await cmd.ExecuteNonQueryAsync();

        (await MarkerExistsAsync()).ShouldBeFalse("the DROP path must not execute injected SQL either");
        (await SequenceCountAsync($"mt_events_sequence_{Payload}")).ShouldBe(0L, "and must still drop the sequence");
    }

    /// <summary>
    /// The name-builder in isolation: an embedded double quote is doubled, which is what
    /// <c>quote_ident</c>/<c>%I</c> does — so the escaped name still denotes the same object the quick-append
    /// function resolves from the raw stored suffix. Hyphens and GUIDs must pass through untouched, since
    /// normalizing them would break that lookup (#4924).
    /// </summary>
    [Fact]
    public void quoted_sequence_name_doubles_embedded_quotes()
    {
        PerTenantEventSequences.QuotedSequenceName("evt", "a\"b")
            .ShouldBe("\"evt\".\"mt_events_sequence_a\"\"b\"");

        PerTenantEventSequences.QuotedSequenceName("evt", "tenant-a")
            .ShouldBe("\"evt\".\"mt_events_sequence_tenant-a\"");

        PerTenantEventSequences.QuotedSequenceName("evt", "6f1a3c2e-5b1d-4f0a-9c3e-8d7b6a5f4e3c")
            .ShouldBe("\"evt\".\"mt_events_sequence_6f1a3c2e-5b1d-4f0a-9c3e-8d7b6a5f4e3c\"");
    }

    /// <summary>
    /// The guard that closes the asymmetry with the DefaultTenancy path. It has to reject what can escape a
    /// quoted identifier or a string literal while still accepting the hyphenated and GUID tenant ids sharded
    /// tenancy has always supported — so it is a narrow denylist, not <c>^[A-Za-z0-9_]+$</c>.
    /// </summary>
    [Theory]
    [InlineData("s1\"; select 1")]        // closes a quoted identifier
    [InlineData("s1'; select 1")]         // closes a string literal (Weasel's partition bound)
    [InlineData("s1\\'; select 1")]       // backslash escape introducer
    [InlineData("s1\n; select 1")]        // newline terminates a -- comment
    [InlineData("s1\0")]                  // NUL
    public void tenant_id_guard_rejects_ddl_unsafe_values(string tenantId)
    {
        Should.Throw<ArgumentException>(() => AdvancedOperations.AssertTenantIdSafeForDdl(tenantId));
    }

    [Theory]
    [InlineData("tenant-a")]
    [InlineData("6f1a3c2e-5b1d-4f0a-9c3e-8d7b6a5f4e3c")]
    [InlineData("acme_corp")]
    [InlineData("Tenant.One")]
    [InlineData("*DEFAULT*")]
    public void tenant_id_guard_accepts_realistic_tenant_ids(string tenantId)
    {
        Should.NotThrow(() => AdvancedOperations.AssertTenantIdSafeForDdl(tenantId));
    }

    /// <summary>
    /// <c>BulkEventAppender</c> writes this sequence name into two different SQL positions: a
    /// <c>nextval('…')</c> / <c>setval('…')</c> string literal, and a bare identifier
    /// (<c>select last_value from …</c>). It used to build an unquoted name, which held up in the first
    /// position only because <c>regclassin</c> is lenient — the bare-identifier position raised
    /// <c>42601 syntax error at or near "-"</c> for the hyphenated and GUID tenant ids that sharded tenancy
    /// has always allowed, so <c>PreserveSourceSequence</c> imports failed for them. The quoted name has to
    /// work in both places, which is what this pins.
    /// </summary>
    [Theory]
    [InlineData("tenant-a")]
    [InlineData("6f1a3c2e-5b1d-4f0a-9c3e-8d7b6a5f4e3c")]
    public async Task quoted_sequence_name_resolves_in_both_sql_positions(string suffix)
    {
        await using var store = BuildStore();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        await PerTenantEventSequences.EnsureSequencesAsync(
            (PostgresqlDatabase)store.Storage.Database, _schema, new[] { suffix }, CancellationToken.None);

        var name = PerTenantEventSequences.QuotedSequenceName(_schema, suffix);

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        // Position 1: inside a single-quoted regclass literal.
        await using (var nextval = conn.CreateCommand())
        {
            nextval.CommandText = $"select nextval('{name.Replace("'", "''")}')";
            (await nextval.ExecuteScalarAsync()).ShouldBe(1L);
        }

        // Position 2: bare identifier in a FROM clause — the one that used to be a syntax error.
        await using (var lastValue = conn.CreateCommand())
        {
            lastValue.CommandText = $"select last_value from {name}";
            (await lastValue.ExecuteScalarAsync()).ShouldBe(1L);
        }
    }

    /// <summary>Plant the row the way sharded tenancy does — the raw tenant id as the partition suffix.</summary>
    private async Task PlantRawSuffixAsync(string suffix)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var insert = conn.CreateCommand();
        insert.CommandText =
            $"insert into {_partitionSchema}.mt_tenant_partitions (partition_value, partition_suffix) " +
            "values (@v, @s) on conflict do nothing";
        insert.Parameters.AddWithValue("v", suffix);
        insert.Parameters.AddWithValue("s", suffix);
        await insert.ExecuteNonQueryAsync();
    }

    private async Task<bool> MarkerExistsAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "select to_regclass('public." + MarkerTable + "') is not null";
        return (bool)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task DropMarkerAsync(NpgsqlConnection? existing = null)
    {
        if (existing != null)
        {
            await using var inner = existing.CreateCommand();
            inner.CommandText = $"drop table if exists public.{MarkerTable}";
            await inner.ExecuteNonQueryAsync();
            return;
        }

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"drop table if exists public.{MarkerTable}";
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<long> SequenceCountAsync(string sequenceName)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "select count(*) from information_schema.sequences where sequence_schema = @s and sequence_name = @n";
        cmd.Parameters.AddWithValue("s", _schema);
        cmd.Parameters.AddWithValue("n", sequenceName);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }
}
