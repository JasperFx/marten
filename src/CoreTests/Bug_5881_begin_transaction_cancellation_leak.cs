#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Transactions;
using Marten;
using Marten.Services;
using Marten.Testing.Harness;
using Microsoft.Extensions.Logging;
using Npgsql;
using Shouldly;
using Xunit;

namespace CoreTests;

/// <summary>
///     #5881. A lightweight session lost its pooled connection when the token handed to
///     <c>BeginTransactionAsync</c> was cancelled after the connection opened.
///     <c>AutoClosingLifetime.StartAsync</c> created a <c>TransactionalConnection</c> and returned it only
///     after its transaction began, so a failure in between left an open connection that nothing owned.
///     Disposing the session, the store and the data source did not release it, and <c>DROP DATABASE</c>
///     failed with 55006 until the process exited.
/// </summary>
/// <remarks>
///     <para>
///         Each test runs against a database of its own, cancels the caller's token from Npgsql's
///         <c>OpenedConnection</c> log event, as the issue's repro does, so the timing does not depend on a delay,
///         and then proves the connection was given back: no backend is left on the database once the pool's idle
///         connections are cleared, and the database can be dropped without <c>FORCE</c>.
///     </para>
///     <para>
///         Counting backends is only meaningful after <see cref="NpgsqlDataSource.Clear" />, because a pool keeps
///         a returned connection open and idle. The drop is the final check, since a backend can outlive the
///         client's close by a moment.
///     </para>
/// </remarks>
public class Bug_5881_begin_transaction_cancellation_leak: IAsyncLifetime
{
    private const string Tenant = "tenant_5881";

    private readonly string _database = $"marten_5881_{Guid.NewGuid().ToString("N")[..8]}";
    private readonly string _admin;

    public Bug_5881_begin_transaction_cancellation_leak()
    {
        _admin = new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString) { Database = "postgres" }
            .ConnectionString;
    }

    public async ValueTask InitializeAsync()
    {
        await adminAsync($"create database {_database}");
    }

    public async ValueTask DisposeAsync()
    {
        // Only reached with something left behind when a test failed
        await adminAsync($"drop database if exists {_database} with (force)");
    }

    [Fact]
    public async Task lightweight_session_begin_transaction()
    {
        await theConnectionIsReleasedAfterCancellation(_ => { }, async (store, token) =>
        {
            await using var session = store.LightweightSession();
            await session.BeginTransactionAsync(token);
        });
    }

    [Fact]
    public async Task identity_session_begin_transaction_with_row_level_security()
    {
        await theConnectionIsReleasedAfterCancellation(opts => opts.UseRowLevelSecurity(), async (store, token) =>
        {
            await using var session = store.IdentitySession(Tenant);
            await session.BeginTransactionAsync(token);
        });
    }

    [Fact]
    public async Task query_session_connection_is_read_only_transactional()
    {
        await theConnectionIsReleasedAfterCancellation(_ => { }, async (store, token) =>
        {
            await using var session = store.QuerySession();
            await session.GetConnectionAsync(token);
        });
    }

    [Fact]
    public async Task query_session_connection_with_row_level_security()
    {
        await theConnectionIsReleasedAfterCancellation(opts => opts.UseRowLevelSecurity(), async (store, token) =>
        {
            await using var session = store.QuerySession(Tenant);
            await session.GetConnectionAsync(token);
        });
    }

    [Fact]
    public async Task open_serializable_session()
    {
        await theConnectionIsReleasedAfterCancellation(_ => { }, async (store, token) =>
        {
            await using var session = await store.OpenSerializableSessionAsync(new SessionOptions(), token);
        });
    }

    [Fact]
    public async Task query_serializable_session()
    {
        await theConnectionIsReleasedAfterCancellation(_ => { }, async (store, token) =>
        {
            await using var session = await store.QuerySerializableSessionAsync(new SessionOptions(), token);
        });
    }

    [Fact]
    public async Task open_serializable_session_with_row_level_security()
    {
        await theConnectionIsReleasedAfterCancellation(opts => opts.UseRowLevelSecurity(), async (store, token) =>
        {
            await using var session =
                await store.OpenSerializableSessionAsync(new SessionOptions { TenantId = Tenant }, token);
        });
    }

    [Fact]
    public async Task query_serializable_session_with_row_level_security()
    {
        await theConnectionIsReleasedAfterCancellation(opts => opts.UseRowLevelSecurity(), async (store, token) =>
        {
            await using var session =
                await store.QuerySerializableSessionAsync(new SessionOptions { TenantId = Tenant }, token);
        });
    }

    /// <summary>
    ///     Not a cancellation: the serializable factory opens the connection and begins the transaction before it
    ///     builds the session, so anything that throws in between left the connection with nobody to dispose it.
    /// </summary>
    [Fact]
    public async Task open_serializable_session_that_fails_after_its_transaction_began()
    {
        await using var scenario = await Scenario.StartAsync(this, _ => { });

        await Should.ThrowAsync<ArgumentOutOfRangeException>(() =>
            scenario.Store.OpenSerializableSessionAsync(new SessionOptions { Tracking = (DocumentTracking)42 }));

        await scenario.ShouldReleaseEverythingAsync();
    }

    /// <summary>
    ///     The auto-closing lifetime opens a connection per command. With row level security it then sets the
    ///     tenant on it, and that call sat outside the block that closes the connection on failure.
    /// </summary>
    [Fact]
    public async Task auto_closing_command_with_row_level_security()
    {
        await theConnectionIsReleasedAfterCancellation(opts => opts.UseRowLevelSecurity(), async (store, token) =>
        {
            await using var session = store.QuerySession(Tenant);
            await session.QueryAsync<int>("select 1", token);
        });
    }

    [Fact]
    public async Task auto_closing_batch_with_row_level_security()
    {
        await theConnectionIsReleasedAfterCancellation(opts => opts.UseRowLevelSecurity(), async (store, token) =>
        {
            await using var session = store.QuerySession(Tenant);
            await foreach (var _ in session.AdvancedSql.StreamAsync<int>("select 1", token))
            {
            }
        });
    }

    /// <summary>
    ///     A sticky lifetime owns its connection, so nothing leaked here. But the connection stayed open after the
    ///     tenant setting failed, and the next command took "already open" to mean "already set up" and ran
    ///     without the tenant.
    /// </summary>
    [Fact]
    public async Task sticky_connection_sets_the_tenant_after_a_cancelled_open()
    {
        await using var scenario = await Scenario.StartAsync(this, opts =>
        {
            opts.UseRowLevelSecurity();
            opts.UseStickyConnectionLifetimes = true;
        });

        await using (var session = scenario.Store.QuerySession(Tenant))
        {
            var token = scenario.Arm();
            await Should.ThrowAsync<OperationCanceledException>(() =>
                session.QueryAsync<string>("select current_setting('app.tenant_id', true)", token));
            scenario.Fired.ShouldBeTrue();

            (await session.QueryAsync<string>("select current_setting('app.tenant_id', true)"))
                .Single().ShouldBe(Tenant);
        }

        await scenario.ShouldReleaseEverythingAsync();
    }

    /// <summary>
    ///     The same shape for a session in an ambient transaction: the connection stayed open, without the tenant
    ///     and outside the transaction, and the next command used it as it was.
    /// </summary>
    [Fact]
    public async Task ambient_transaction_sets_the_tenant_after_a_cancelled_open()
    {
        await using var scenario = await Scenario.StartAsync(this, opts => opts.UseRowLevelSecurity());

        using (var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
        {
            var options = SessionOptions.ForCurrentTransaction();
            options.TenantId = Tenant;

            await using var session = scenario.Store.QuerySession(options);

            var token = scenario.Arm();
            await Should.ThrowAsync<OperationCanceledException>(() =>
                session.QueryAsync<string>("select current_setting('app.tenant_id', true)", token));
            scenario.Fired.ShouldBeTrue();

            (await session.QueryAsync<string>("select current_setting('app.tenant_id', true)"))
                .Single().ShouldBe(Tenant);
        }

        await scenario.ShouldReleaseEverythingAsync();
    }

    private async Task theConnectionIsReleasedAfterCancellation(Action<StoreOptions> configure,
        Func<IDocumentStore, CancellationToken, Task> act)
    {
        await using var scenario = await Scenario.StartAsync(this, configure);

        var token = scenario.Arm();
        await Should.ThrowAsync<OperationCanceledException>(() => act(scenario.Store, token));
        scenario.Fired.ShouldBeTrue("the cancellation never fired, so this test proved nothing");

        await scenario.ShouldReleaseEverythingAsync();
    }

    private async Task adminAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_admin);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<long> countBackendsAsync()
    {
        await using var connection = new NpgsqlConnection(_admin);
        await connection.OpenAsync();
        await using var command =
            new NpgsqlCommand("select count(*) from pg_stat_activity where datname = @database", connection);
        command.Parameters.AddWithValue("database", _database);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private sealed class Scenario: IAsyncDisposable
    {
        private readonly Bug_5881_begin_transaction_cancellation_leak _parent;
        private readonly CancelOnOpenedConnection _observer = new();
        private readonly ILoggerFactory _loggerFactory;
        private readonly NpgsqlDataSource _dataSource;
        private bool _released;

        private Scenario(Bug_5881_begin_transaction_cancellation_leak parent, Action<StoreOptions> configure)
        {
            _parent = parent;
            _loggerFactory = LoggerFactory.Create(x => x.SetMinimumLevel(LogLevel.Debug).AddProvider(_observer));

            var builder = new NpgsqlDataSourceBuilder(
                new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString) { Database = parent._database }
                    .ConnectionString);
            builder.UseLoggerFactory(_loggerFactory);
            _dataSource = builder.Build();

            Store = DocumentStore.For(opts =>
            {
                opts.Connection(_dataSource);
                configure(opts);
            });
        }

        public DocumentStore Store { get; }

        public bool Fired => _observer.Fired;

        public static async Task<Scenario> StartAsync(Bug_5881_begin_transaction_cancellation_leak parent,
            Action<StoreOptions> configure)
        {
            var scenario = new Scenario(parent, configure);

            // Warm the pool and Npgsql's type loading so the armed open is an ordinary one
            await using (var session = scenario.Store.QuerySession(Tenant))
            {
                await session.QueryAsync<int>("select 1");
            }

            return scenario;
        }

        public CancellationToken Arm()
        {
            var cancellation = new CancellationTokenSource();
            _observer.Arm(cancellation);
            return cancellation.Token;
        }

        public async Task ShouldReleaseEverythingAsync()
        {
            // Idle pooled connections are not a leak; only one still checked out is
            _dataSource.Clear();

            var backends = await waitForBackendsToExitAsync();
            backends.ShouldBe(0, "a connection is still checked out after the session was disposed");

            await releaseAsync();

            // The final external check: without FORCE, this fails with 55006 while anything is connected
            await _parent.adminAsync($"drop database {_parent._database}");
        }

        private async Task<long> waitForBackendsToExitAsync()
        {
            long backends = 0;
            for (var i = 0; i < 50; i++)
            {
                backends = await _parent.countBackendsAsync();
                if (backends == 0) return 0;
                await Task.Delay(100);
            }

            return backends;
        }

        private async Task releaseAsync()
        {
            if (_released) return;
            _released = true;

            Store.Dispose();
            await _dataSource.DisposeAsync();
            _loggerFactory.Dispose();
        }

        public async ValueTask DisposeAsync()
        {
            await releaseAsync();
        }
    }

    private sealed class CancelOnOpenedConnection: ILoggerProvider, ILogger
    {
        private CancellationTokenSource? _armed;

        public bool Fired { get; private set; }

        public void Arm(CancellationTokenSource source)
        {
            Fired = false;
            _armed = source;
        }

        public ILogger CreateLogger(string categoryName) => this;

        public bool IsEnabled(LogLevel logLevel) => true;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (eventId.Name != "OpenedConnection") return;

            var armed = Interlocked.Exchange(ref _armed, null);
            if (armed == null) return;

            Fired = true;
            armed.Cancel();
        }

        public void Dispose()
        {
        }
    }
}
