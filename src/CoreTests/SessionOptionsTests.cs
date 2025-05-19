using System;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.CodeGeneration;
using JasperFx.Core;
using Marten;
using Marten.Internal.OpenTelemetry;
using Marten.Internal.Sessions;
using Marten.Services;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using NSubstitute;
using Shouldly;
using Xunit;

namespace CoreTests;

public class SessionOptionsTests: OneOffConfigurationsContext
{
    #region sample_ConfigureCommandTimeout

    public void ConfigureCommandTimeout(IDocumentStore store)
    {
        // Sets the command timeout for this session to 60 seconds
        // The default is 30
        using (var session = store.LightweightSession(new SessionOptions { Timeout = 60 }))
        {
        }
    }

    #endregion

    [Fact]
    public void for_connection_string()
    {
        var options = SessionOptions.ForConnectionString(ConnectionSource.ConnectionString);

        options.Connection?.ConnectionString.ShouldBe(ConnectionSource.ConnectionString);
        options.Connection?.State.ShouldBe(ConnectionState.Closed);

        options.OwnsConnection.ShouldBeTrue();
        options.OwnsTransactionLifecycle.ShouldBeTrue();
    }

    [Fact]
    public void for_database()
    {
        var database = Substitute.For<IMartenDatabase>();
        var options = SessionOptions.ForDatabase(database);

        options.Tenant?.Database.ShouldBeSameAs(database);
        options.Tenant?.TenantId.ShouldBe(StorageConstants.DefaultTenantId);
        options.OwnsConnection.ShouldBeTrue();
        options.OwnsTransactionLifecycle.ShouldBeTrue();
    }

    [Fact]
    public void with_tracking()
    {
        new SessionOptions().WithTracking(DocumentTracking.None)
            .Tracking.ShouldBe(DocumentTracking.None);
    }

    [Fact]
    public void add_a_listener()
    {
        var options = new SessionOptions();
        var listener = Substitute.For<IDocumentSessionListener>();
        options.ListenAt(listener)
            .Listeners.Single().ShouldBe(listener);
    }


    [Fact]
    public async Task for_transaction()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync();

        var options = SessionOptions.ForTransaction(tx);
        options.Transaction.ShouldBe(tx);
        options.Connection.ShouldBe(conn);
        options.OwnsConnection.ShouldBeFalse();
        options.OwnsTransactionLifecycle.ShouldBeFalse();
    }

    [Fact]
    public void build_from_connection()
    {
        var connection = new NpgsqlConnection(ConnectionSource.ConnectionString);
        var options = SessionOptions.ForConnection(connection);
        options.Connection.ShouldBe(connection);
        options.Timeout!.Value.ShouldBe(connection.CommandTimeout);
        options.OwnsConnection.ShouldBeTrue(); // if the connection is closed
        options.OwnsTransactionLifecycle.ShouldBeTrue();
    }

    [Fact]
    public async Task build_from_open_connection()
    {
        await using var connection = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await connection.OpenAsync();
        var options = SessionOptions.ForConnection(connection);
        options.Connection.ShouldBe(connection);
        options.Timeout!.Value.ShouldBe(connection.CommandTimeout);
        options.OwnsConnection.ShouldBeFalse(); // if the connection is closed
        options.OwnsTransactionLifecycle.ShouldBeTrue();
    }

    [Fact]
    public async Task for_transaction_with_ownership()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync();

        var options = SessionOptions.ForTransaction(tx, true);
        options.Transaction.ShouldBe(tx);
        options.Connection.ShouldBe(conn);
        options.OwnsConnection.ShouldBeFalse();
        options.OwnsTransactionLifecycle.ShouldBeTrue();
        options.Timeout.ShouldBe(conn.CommandTimeout);
    }


    [Fact]
    public void the_default_concurrency_checks_is_enabled()
    {
        new SessionOptions().ConcurrencyChecks
            .ShouldBe(ConcurrencyChecks.Enabled);
    }

    [Fact]
    public async Task can_choke_on_custom_timeout()
    {
        var options = new SessionOptions { Timeout = 1 };

        using var session = theStore.LightweightSession(options);
        var e = await Should.ThrowAsync<Marten.Exceptions.MartenCommandException>(async () =>
        {
            await session.QueryAsync<FryGuy>("select pg_sleep(2)");
        });

        Assert.Contains("Timeout during reading attempt", e.InnerException?.InnerException?.Message);
    }

    [Fact]
    public async Task default_timeout_should_be_npgsql_default_ie_30()
    {
        // TODO -- do this without the Preview command. Check against the session itself
        StoreOptions(opts =>
        {
            var connectionString = ConnectionSource.ConnectionString.Replace(";Command Timeout=5", "");
            opts.Connection(connectionString);
            opts.GeneratedCodeMode = TypeLoadMode.Auto;
        });

        var options = new SessionOptions();

        using var query = theStore.QuerySession(options);
        var cmd = await query.Query<FryGuy>().ExplainAsync();
        Assert.Equal(30, cmd.Command.CommandTimeout);
    }


    [Fact]
    public async Task can_define_custom_timeout()
    {
        // TODO -- do this without the Preview command. Check against the session itself
        var options = new SessionOptions { Timeout = 15 };

        using var query = theStore.QuerySession(options);
        var cmd = await query.Query<FryGuy>().ExplainAsync();

        Assert.Equal(15, cmd.Command.CommandTimeout);
    }

    [Fact]
    public async Task can_define_custom_timeout_via_pgcstring()
    {
        var connectionStringBuilder = new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString);

        connectionStringBuilder.CommandTimeout = 1;

        var documentStore = DocumentStore.For(c =>
        {
            c.Connection(connectionStringBuilder.ToString());
        });

        using var query = documentStore.LightweightSession();
        var cmd = await query.Query<FryGuy>().ExplainAsync();
        Assert.Equal(1, cmd.Command.CommandTimeout);
        Assert.Equal(1, query.Connection?.CommandTimeout);
    }

    [Fact]
    public async Task can_override_pgcstring_timeout_in_sessionoptions()
    {
        var connectionStringBuilder = new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString);

        connectionStringBuilder.CommandTimeout = 1;

        var documentStore = DocumentStore.For(c =>
        {
            c.Connection(connectionStringBuilder.ToString());
        });

        var options = new SessionOptions { Timeout = 60 };

        using var query = documentStore.QuerySession(options);
        var cmd = await query.Query<FryGuy>().ExplainAsync();
        Assert.Equal(60, cmd.Command.CommandTimeout);
        Assert.Equal(1, query.Connection?.CommandTimeout);
    }

    [Fact]
    public async Task session_with_custom_connection_reusable_after_saveChanges()
    {
        var connectionStringBuilder = new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString);

        var documentStore = DocumentStore.For(c =>
        {
            c.Connection(connectionStringBuilder.ToString());
        });

        var connection = new NpgsqlConnection(connectionStringBuilder.ToString());
        connection.Open();

        var options = new SessionOptions { Connection = connection };

        var testObject = new FryGuy();

        using var session = documentStore.LightweightSession(options);
        session.Store(testObject);
        await session.SaveChangesAsync();
        (await session.LoadAsync<FryGuy>(testObject.Id)).ShouldNotBeNull();
    }

    [Fact]
    public async Task session_with_custom_connection_reusable_after_saveChangesAsync()
    {
        var connectionStringBuilder = new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString);

        var documentStore = DocumentStore.For(c =>
        {
            c.Connection(connectionStringBuilder.ToString());
        });

        var connection = new NpgsqlConnection(connectionStringBuilder.ToString());
        connection.Open();

        var options = new SessionOptions { Connection = connection };

        var testObject = new FryGuy();

        await using var query = documentStore.LightweightSession(options);
        query.Store(testObject);
        await query.SaveChangesAsync();
        var loadedObject = await query.LoadAsync<FryGuy>(testObject.Id);
        loadedObject.ShouldNotBeNull();
    }

    [Fact]
    public void build_connection_by_default()
    {
        using var store = DocumentStore.For(ConnectionSource.ConnectionString);

        var options = new SessionOptions{Timeout = 7};
        options.Initialize(store, CommandRunnerMode.Transactional, new OpenTelemetryOptions(){ TrackConnections = TrackLevel.None })
            .ShouldBeOfType<AutoClosingLifetime>()
            .CommandTimeout.ShouldBe(7);
    }

    [Fact]
    public void build_connection_with_sticky_connections_enabled()
    {
        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.UseStickyConnectionLifetimes = true;
        });

        var options = new SessionOptions{Timeout = 2};
        options.Initialize(store, CommandRunnerMode.Transactional, new OpenTelemetryOptions(){ TrackConnections = TrackLevel.None })
            .ShouldBeOfType<TransactionalConnection>()
            .CommandTimeout.ShouldBe(2)
            ;
    }

    [Fact]
    public void Session_Should_Not_Track_Open_Telemetry_Events_By_Default()
    {
        var commandRunnerMode = CommandRunnerMode.ReadOnly;
        var options = new SessionOptions();
        var connectionLifetime = options.Initialize(theStore, commandRunnerMode, new OpenTelemetryOptions(){ TrackConnections = TrackLevel.None });
        connectionLifetime.ShouldNotBeOfType<EventTracingConnectionLifetime>();
    }

    [Fact]
    public void Session_Should_Not_Track_Open_Telemetry_Events_When_Asked_To_Do_So_If_No_Listeners_Are_configured()
    {
        var commandRunnerMode = CommandRunnerMode.ReadOnly;
        var options = new SessionOptions();
        var connectionLifetime = options.Initialize(theStore, commandRunnerMode, new OpenTelemetryOptions(){ TrackConnections = TrackLevel.Normal });
        connectionLifetime.ShouldNotBeOfType<EventTracingConnectionLifetime>();
    }

    [Fact]
    public void Session_Should_Track_Open_Telemetry_Events_When_Asked_To_Do_So_If_Listeners_Are_configured()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => _.Name == "Marten",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);
            var commandRunnerMode = CommandRunnerMode.ReadOnly;
        var options = new SessionOptions();
        var connectionLifetime = options.Initialize(theStore, commandRunnerMode, new OpenTelemetryOptions(){ TrackConnections = TrackLevel.Normal });
        connectionLifetime.ShouldBeOfType<EventTracingConnectionLifetime>();
    }

    public class FryGuy
    {
        public Guid Id;
    }
}
