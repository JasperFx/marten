using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Microsoft.Extensions.Logging;
using Npgsql;
using Shouldly;
using Xunit;

namespace CoreTests;

/// <summary>
///     With <c>DisableNpgsqlLogging = false</c>, every data source Marten builds forwards Npgsql's
///     logging to the store's <see cref="ILoggerFactory" />. <c>ConfigureNpgsqlDataSourceBuilder()</c>
///     used to rebuild the data source factory from a builder factory that never wired the logger in,
///     so registering pgvector, NodaTime or the DCB HStore mode silently turned Npgsql logging off.
/// </summary>
public class npgsql_logging_survives_configured_data_source_builders
{
    [Fact]
    public async Task logs_through_the_logger_factory_by_default()
    {
        var recorder = new NpgsqlLogRecorder();
        var options = optionsLoggingTo(recorder);

        await openConnectionAsync(options, ConnectionSource.ConnectionString);

        recorder.OpenedConnections.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task logs_through_the_logger_factory_after_configuring_the_data_source_builder()
    {
        var recorder = new NpgsqlLogRecorder();
        var options = optionsLoggingTo(recorder);

        options.ConfigureNpgsqlDataSourceBuilder(_ => { });

        await openConnectionAsync(options, ConnectionSource.ConnectionString);

        recorder.OpenedConnections.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task logs_through_the_logger_factory_when_the_logger_factory_is_set_afterwards()
    {
        // AddMarten() assigns the LogFactory after the user's configuration has run
        var recorder = new NpgsqlLogRecorder();
        var options = new StoreOptions { DisableNpgsqlLogging = false };
        options.ConfigureNpgsqlDataSourceBuilder(_ => { });

        options.LogFactory = recorder.Factory;

        await openConnectionAsync(options, ConnectionSource.ConnectionString);

        recorder.OpenedConnections.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task logs_for_a_marten_built_data_source_beside_a_supplied_one()
    {
        // The caller owns the supplied data source, but any other connection string (a tenant
        // database, say) gets a data source Marten builds, and that one should log
        var recorder = new NpgsqlLogRecorder();
        var options = optionsLoggingTo(recorder);

        await using var supplied = NpgsqlDataSource.Create(ConnectionSource.ConnectionString);
        options.Connection(supplied);
        options.ConfigureNpgsqlDataSourceBuilder(_ => { });

        var other = new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString)
        {
            ApplicationName = "npgsql_logging_tenant"
        }.ConnectionString;

        await openConnectionAsync(options, other);

        recorder.OpenedConnections.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task stays_quiet_when_npgsql_logging_is_disabled()
    {
        var recorder = new NpgsqlLogRecorder();
        var options = optionsLoggingTo(recorder);
        options.DisableNpgsqlLogging = true;

        options.ConfigureNpgsqlDataSourceBuilder(_ => { });

        await openConnectionAsync(options, ConnectionSource.ConnectionString);

        recorder.OpenedConnections.ShouldBe(0);
    }

    [Fact]
    public async Task still_applies_the_configured_builder_action()
    {
        var recorder = new NpgsqlLogRecorder();
        var options = optionsLoggingTo(recorder);

        var applied = 0;
        options.ConfigureNpgsqlDataSourceBuilder(_ => applied++);

        await openConnectionAsync(options, ConnectionSource.ConnectionString);

        applied.ShouldBe(1);
        recorder.OpenedConnections.ShouldBeGreaterThan(0);
    }

    private static StoreOptions optionsLoggingTo(NpgsqlLogRecorder recorder)
    {
        return new StoreOptions { DisableNpgsqlLogging = false, LogFactory = recorder.Factory };
    }

    private static async Task openConnectionAsync(StoreOptions options, string connectionString)
    {
        // A throwaway StoreOptions owns nothing else, so dispose the data source it built here
        await using var dataSource = options.NpgsqlDataSourceFactory.Create(connectionString);

        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("select 1", connection);
        await command.ExecuteScalarAsync();
    }

    private sealed class NpgsqlLogRecorder: ILoggerProvider, ILogger
    {
        private readonly ConcurrentQueue<string> _events = new();

        public NpgsqlLogRecorder()
        {
            Factory = LoggerFactory.Create(x => x.SetMinimumLevel(LogLevel.Trace).AddProvider(this));
        }

        public ILoggerFactory Factory { get; }

        public int OpenedConnections
        {
            get
            {
                var count = 0;
                foreach (var name in _events)
                {
                    if (name == "OpenedConnection") count++;
                }

                return count;
            }
        }

        public ILogger CreateLogger(string categoryName) => this;

        public bool IsEnabled(LogLevel logLevel) => true;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (eventId.Name != null) _events.Enqueue(eventId.Name);
        }

        public void Dispose()
        {
        }
    }
}
