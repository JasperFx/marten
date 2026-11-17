using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events.Daemon;
using Marten;
using Marten.Events.Daemon.Internals;
using Marten.Internal.Sessions;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DaemonTests;

/// <summary>
///     A projection that loads side documents by id through the daemon's <see cref="ProjectionDocumentSession" />
///     must not query for an empty id list. With <c>UseIdentityMapForAggregates</c> off, that session loads on a
///     connection of its own (<c>LoadManyProjectedAsync</c>), so the reads are counted from Npgsql's activities rather
///     than the session's request count.
/// </summary>
public class projection_session_load_many_with_no_ids: OneOffConfigurationsContext
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task no_ids_send_no_command(bool useIdentityMapForAggregates)
    {
        StoreOptions(opts =>
        {
            opts.Events.UseIdentityMapForAggregates = useIdentityMapForAggregates;
            opts.Schema.For<Target>();
        });
        // Create the table up front so schema checks are not part of what is measured.
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var batch = new ProjectionUpdateBatch(theStore.Options.Projections,
            (DocumentSessionBase)theSession, ShardExecutionMode.Continuous, CancellationToken.None);
        var sessionOptions = SessionOptions.ForDatabase(theStore.Options.Tenancy.Default.Database);
        await using var projectionSession = new ProjectionDocumentSession(theStore, batch,
            sessionOptions, ShardExecutionMode.Continuous);

        using var commands = new NpgsqlCommandRecorder(
            theStore.Options.Storage.MappingFor(typeof(Target)).TableName.QualifiedName);
        (await projectionSession.LoadManyAsync<Target>(Array.Empty<Guid>())).ShouldBeEmpty();
        commands.Statements.ShouldBeEmpty();

        // The recorder sees the load once there is an id to look up.
        (await projectionSession.LoadManyAsync<Target>(Guid.NewGuid())).ShouldBeEmpty();
        commands.Statements.ShouldHaveSingleItem();
    }

    private sealed class NpgsqlCommandRecorder: IDisposable
    {
        private readonly ActivityListener _listener;
        private readonly string _table;
        private readonly ConcurrentQueue<string> _statements = new();

        public NpgsqlCommandRecorder(string qualifiedTableName)
        {
            _table = qualifiedTableName;
            _listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == "Npgsql",
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = onStopped
            };
            ActivitySource.AddActivityListener(_listener);
        }

        public string[] Statements => _statements.ToArray();

        private void onStopped(Activity activity)
        {
            // Npgsql 9 tags the command text as db.statement, Npgsql 10 as db.query.text.
            var sql = activity.GetTagItem("db.statement") as string ?? activity.GetTagItem("db.query.text") as string;
            if (sql != null && sql.Contains(_table, StringComparison.OrdinalIgnoreCase)) _statements.Enqueue(sql);
        }

        public void Dispose() => _listener.Dispose();
    }
}
