using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using Marten;
using Marten.Testing.Harness;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace DaemonTests.Resiliency;

/// <summary>
/// marten#5229: MartenDatabase's explicit IEventDatabase.StoreDeadLetterEventAsync() used to wrap its
/// whole body in `catch (Exception) { }` with a "TODO -- something to log this?". That made two
/// different failures indistinguishable from success:
///
/// 1. A wrong `storage` argument. The parameter is typed `object` because each store reads it
///    differently, so nothing catches it at compile time -- and the cast exception went into the
///    swallow, leaving a caller with a method that returned normally having written nothing.
/// 2. A genuine write failure. The dead letter -- the only record that a projection skipped an
///    event -- was dropped with no exception, no log line and nothing to grep for.
/// </summary>
public class Bug_5229_dead_letter_storage_failures: OneOffConfigurationsContext
{
    private static DeadLetterEvent aDeadLetter() =>
        new()
        {
            Id = Guid.CreateVersion7(),
            ProjectionName = "Trip",
            ShardName = "All",
            EventSequence = 42,
            TenantId = "*DEFAULT*",
            Timestamp = DateTimeOffset.UtcNow,
            ExceptionType = "InvalidOperationException",
            ExceptionMessage = "boom"
        };

    [Fact]
    public async Task passing_something_other_than_the_document_store_throws()
    {
        var database = (IEventDatabase)theStore.Tenancy.Default.Database;

        // An IDocumentSession is a perfectly plausible reading of a parameter called "storage" on a
        // method that writes a row. Before #5229 this was a silent no-op.
        var ex = await Should.ThrowAsync<ArgumentException>(async () =>
            await database.StoreDeadLetterEventAsync(theSession, aDeadLetter(), CancellationToken.None));

        ex.ParamName.ShouldBe("storage");
        ex.Message.ShouldContain(nameof(DocumentStore));

        await Should.ThrowAsync<ArgumentException>(async () =>
            await database.StoreDeadLetterEventAsync(null, aDeadLetter(), CancellationToken.None));

        // ...and, the actual symptom that made this so hard to find, nothing was written.
        await using var query = theStore.QuerySession();
        (await query.Query<DeadLetterEvent>().CountAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
    }

    [Fact]
    public async Task the_document_store_is_what_the_method_actually_wants()
    {
        var database = (IEventDatabase)theStore.Tenancy.Default.Database;
        var deadLetter = aDeadLetter();

        await database.StoreDeadLetterEventAsync(theStore, deadLetter, CancellationToken.None);

        await using var query = theStore.QuerySession();
        var all = await query.Query<DeadLetterEvent>().ToListAsync(TestContext.Current.CancellationToken);

        all.ShouldHaveSingleItem().EventSequence.ShouldBe(deadLetter.EventSequence);
    }

    [Fact]
    public async Task a_failure_to_persist_is_logged_instead_of_being_swallowed()
    {
        var logger = new CapturingLogger();

        // AutoCreate.None against a schema that was never built: the dead letter table does not
        // exist, so SaveChangesAsync() fails. This stands in for the real cases -- a connection
        // failure, or the table not having been created yet -- where a dead letter is genuinely lost.
        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "dead_letters_5229_never_created";
            opts.AutoCreateSchemaObjects = AutoCreate.None;
            opts.DotNetLogger = logger;
        });

        var database = (IEventDatabase)store.Tenancy.Default.Database;
        var deadLetter = aDeadLetter();

        // Still does not throw -- the daemon must not fall over because it could not record a
        // dead letter, and the RetryBlock around this call site has already had its say.
        await database.StoreDeadLetterEventAsync(store, deadLetter, CancellationToken.None);

        var error = logger.Errors.ShouldHaveSingleItem();

        error.Exception.ShouldNotBeNull();
        error.Message.ShouldContain("dead letter");
        error.Message.ShouldContain(deadLetter.ProjectionName);
        error.Message.ShouldContain(deadLetter.ShardName);
        error.Message.ShouldContain(deadLetter.EventSequence.ToString());
    }

    /// <summary>
    /// Only keeps what the assertions above need: the formatted message and the exception of every
    /// Error-level entry.
    /// </summary>
    internal class CapturingLogger: ILogger
    {
        public List<(string Message, Exception? Exception)> Errors { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NoopScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel < LogLevel.Error) return;

            lock (Errors)
            {
                Errors.Add((formatter(state, exception), exception));
            }
        }

        private class NoopScope: IDisposable
        {
            public static readonly NoopScope Instance = new();
            public void Dispose() { }
        }
    }
}
