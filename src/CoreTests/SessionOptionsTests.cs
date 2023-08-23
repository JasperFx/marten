using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.CodeGeneration;
using Marten;
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

        options.Tenant?.Database.ShouldBeTheSameAs(database);
        options.Tenant?.TenantId.ShouldBe(Tenancy.DefaultTenantId);
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
    public void can_choke_on_custom_timeout()
    {
        var options = new SessionOptions { Timeout = 1 };

        using var session = theStore.LightweightSession(options);
        var e = Assert.Throws<Marten.Exceptions.MartenCommandException>(() =>
        {
            session.Query<FryGuy>("select pg_sleep(2)");
        });

        Assert.Contains("Timeout during reading attempt", e.InnerException?.InnerException?.Message);
    }

    [Fact]
    public async Task should_map_exception_correctly_for_invalid_connection_string()
    {
        var store = DocumentStore.For("***");
        await using var session = store.LightweightSession();

        var e = await Assert.ThrowsAsync<Marten.Exceptions.InvalidConnectionStringException>(() =>
            session.QueryAsync<int>("select count(*) from mt_doc_foo")
        );

        Assert.Contains("Invalid connection string", e.Message);
    }

    [Fact]
    public void default_timeout_should_be_npgsql_default_ie_30()
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
        var cmd = query.Query<FryGuy>().Explain();
        Assert.Equal(30, cmd.Command.CommandTimeout);
    }


    [Fact]
    public void can_define_custom_timeout()
    {
        // TODO -- do this without the Preview command. Check against the session itself
        var options = new SessionOptions { Timeout = 15 };

        using var query = theStore.QuerySession(options);
        var cmd = query.Query<FryGuy>().Explain();
        Assert.Equal(15, cmd.Command.CommandTimeout);
    }

    [Fact]
    public void can_define_custom_timeout_via_pgcstring()
    {
        // TODO -- do this without the Preview command. Check against the session itself
        var connectionStringBuilder = new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString);

        connectionStringBuilder.CommandTimeout = 1;

        var documentStore = DocumentStore.For(c =>
        {
            c.Connection(connectionStringBuilder.ToString());
        });

        using var query = documentStore.LightweightSession();
        var cmd = query.Query<FryGuy>().Explain();
        Assert.Equal(1, cmd.Command.CommandTimeout);
        Assert.Equal(1, query.Connection?.CommandTimeout);
    }

    [Fact]
    [Obsolete("Obsolete")]
    public void can_override_pgcstring_timeout_in_sessionoptions()
    {
        var connectionStringBuilder = new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString);

        connectionStringBuilder.CommandTimeout = 1;

        var documentStore = DocumentStore.For(c =>
        {
            c.Connection(connectionStringBuilder.ToString());
        });

        var options = new SessionOptions { Timeout = 60 };

        using var query = documentStore.QuerySession(options);
        var cmd = query.Query<FryGuy>().Explain();
        Assert.Equal(60, cmd.Command.CommandTimeout);
        Assert.Equal(1, query.Connection?.CommandTimeout);
    }

    [Fact]
    public void session_with_custom_connection_reusable_after_saveChanges()
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
        session.SaveChanges();
        session.Load<FryGuy>(testObject.Id).ShouldNotBeNull();
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

    public class FryGuy
    {
        public Guid Id;
    }
}
