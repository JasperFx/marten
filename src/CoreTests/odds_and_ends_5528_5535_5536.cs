using System;
using System.Data;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Core;
using Marten;
using Marten.Internal.Sessions;
using Marten.Services;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Weasel.Core.Migrations;
using Weasel.Postgresql;
using Xunit;

namespace CoreTests;

/// <summary>
/// #5536. A schema name longer than PostgreSQL's 63-character identifier limit was silently truncated by
/// the server, after which Marten kept looking for its objects under the name it asked for, found none,
/// decided everything was missing, and re-issued DDL the server resolved against the truncated schema.
/// The first apply passed and the second failed with
/// <c>42710: constraint "fkey_mt_events_stream_id" for relation "mt_events" already exists</c> — an error
/// that points nowhere near the schema name.
/// </summary>
public class over_long_schema_names_match_what_postgres_stores
{
    private static string NameOfLength(int n) => new string('a', n);

    [Fact]
    public void a_name_at_the_limit_is_untouched()
    {
        var options = new StoreOptions();
        var name = NameOfLength(63);

        options.DatabaseSchemaName = name;

        options.DatabaseSchemaName.ShouldBe(name);
    }

    /// <summary>
    /// The actual defect: Marten used to keep the full name and then look for objects PostgreSQL had stored
    /// under the truncated one.
    /// </summary>
    [Fact]
    public void an_over_long_name_is_truncated_to_what_the_server_will_store()
    {
        var options = new StoreOptions();

        options.DatabaseSchemaName = NameOfLength(80);

        options.DatabaseSchemaName.Length.ShouldBe(63);
        options.DatabaseSchemaName.ShouldBe(NameOfLength(63));
    }

    /// <summary>
    /// The real-world shape, and the one that produced the 42710: a name derived from a long type name.
    /// </summary>
    [Fact]
    public void the_name_that_reproduced_5536_now_matches_the_stored_schema()
    {
        var options = new StoreOptions();

        options.DatabaseSchemaName = "bug_4185_codegen_conflict_projection_with_secondary_store_dependency";

        // Exactly what `select nspname from pg_namespace` reported after the first apply.
        options.DatabaseSchemaName.ShouldBe("bug_4185_codegen_conflict_projection_with_secondary_store_depen");
    }

    /// <summary>
    /// Lower-casing still happens, and happens before the truncation, so the result is the identifier
    /// PostgreSQL would arrive at from the same input.
    /// </summary>
    [Fact]
    public void lower_casing_happens_before_truncation()
    {
        var options = new StoreOptions();

        options.DatabaseSchemaName = new string('A', 80);

        options.DatabaseSchemaName.ShouldBe(NameOfLength(63));
    }

    /// <summary>
    /// End to end, and the fact that actually failed before: applying the same event-store schema twice must
    /// be a no-op the second time. Nothing asserted this, which is why a re-apply that could never settle went
    /// unnoticed.
    /// </summary>
    [Fact]
    public async Task applying_an_event_store_schema_twice_is_a_no_op_the_second_time()
    {
        var schema = "oddsends_" + Guid.NewGuid().ToString("N")[..8];

        for (var pass = 1; pass <= 2; pass++)
        {
            await using var store = DocumentStore.For(opts =>
            {
                opts.Connection(ConnectionSource.ConnectionString);
                opts.DatabaseSchemaName = schema;
                opts.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;
                opts.Events.AddEventType<SchemaReapplyHappened>();
            });

            foreach (PostgresqlDatabase db in await store.Tenancy.BuildDatabases())
            {
                var difference = await db.ApplyAllConfiguredChangesToDatabaseAsync();
                if (pass == 2)
                {
                    difference.ShouldBe(SchemaPatchDifference.None);
                }
            }
        }
    }

    public record SchemaReapplyHappened(string Name);
}

/// <summary>
/// #5529. The apply-changes advisory lock had a fixed 50/100/250ms retry ladder, so a store got about
/// 400ms to win it. Enough for the rolling-deploy case it was built for — outlasting a peer's finished
/// migration — and not enough when more than one store shares a database in one host and the loser has to
/// outlast a migration that is still running. Bobcat's retry ledger caught exactly that after Weasel
/// 9.37.0 stopped stranding the lock: attempt 1 "Unable to attain the global lock in time", attempt 2 fine.
/// </summary>
public class the_apply_changes_lock_timeout
{
    [Fact]
    public void defaults_to_something_a_running_migration_can_fit_inside()
    {
        // The old ceiling was ~400ms of total waiting, which a real migration blows straight through.
        new StoreOptions().ApplyChangesLockTimeout.ShouldBe(10.Seconds());
    }

    [Fact]
    public void is_configurable_for_a_host_with_many_stores_or_a_large_schema()
    {
        var options = new StoreOptions { ApplyChangesLockTimeout = 45.Seconds() };

        options.ApplyChangesLockTimeout.ShouldBe(45.Seconds());
    }
}

/// <summary>
/// #5528. Marten replays the operations it already computed, not the application code that computed them.
/// From RepeatableRead upwards the session's transaction is pinned to one snapshot, so any successful
/// replay commits decisions derived from a snapshot that no longer exists — the lost update #5528 fixed
/// for 40001, and structurally the same for every other error that aborts such a transaction.
/// </summary>
public class write_retries_are_skipped_at_high_isolation : IntegrationContext
{
    public write_retries_are_skipped_at_high_isolation(DefaultStoreFixture fixture) : base(fixture)
    {
    }

    [Theory]
    [InlineData(IsolationLevel.ReadCommitted, false)]
    [InlineData(IsolationLevel.ReadUncommitted, false)]
    [InlineData(IsolationLevel.RepeatableRead, true)]
    [InlineData(IsolationLevel.Serializable, true)]
    [InlineData(IsolationLevel.Snapshot, true)]
    public void the_retry_pipeline_is_skipped_only_from_repeatable_read_upwards(
        IsolationLevel level, bool expectedToSkip)
    {
        using var session = theStore.LightweightSession(new SessionOptions { IsolationLevel = level });

        ((DocumentSessionBase)session).retriesAreUnsoundForThisSession().ShouldBe(expectedToSkip);
    }

    /// <summary>
    /// ReadCommitted is the default, and the one every existing application is on. Pinned separately so a
    /// future change to the set above cannot quietly take retries away from it.
    /// </summary>
    [Fact]
    public void the_default_session_keeps_its_retries()
    {
        using var session = theStore.LightweightSession();

        ((DocumentSessionBase)session).retriesAreUnsoundForThisSession().ShouldBeFalse();
    }
}
