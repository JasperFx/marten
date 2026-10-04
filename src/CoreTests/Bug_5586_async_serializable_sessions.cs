#nullable enable
using System;
using System.Data;
using System.Threading.Tasks;
using Marten;
using Marten.Services;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace CoreTests;

public class Bug5586Doc
{
    public Guid Id { get; set; }
    public int Value { get; set; }
}

/// <summary>
/// #5586. QuerySerializableSessionAsync / OpenSerializableSessionAsync built their lifetime through
/// SessionOptions.InitializeAsync, which — unlike its synchronous twin — never returned an
/// AutoClosingLifetime, never consulted UseStickyConnectionLifetimes, and began a transaction only when
/// IsolationLevel happened to be Serializable.
///
/// SessionOptions.IsolationLevel defaults to ReadCommitted and is not nullable, so passing
/// `new SessionOptions()` to a method with "Serializable" in its name produced a session that was neither:
/// not serializable, and not auto-closing either — just one connection held open for the session's whole
/// life with no transaction protecting it.
/// </summary>
public class Bug_5586_async_serializable_sessions: OneOffConfigurationsContext
{
    [Fact]
    public async Task query_session_with_default_options_is_actually_serializable()
    {
        await theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(Bug5586Doc));

        await using var session = await theStore.QuerySerializableSessionAsync(new SessionOptions());

        // a transaction exists from the start, not from the first command
        (await readIsolationLevel(session)).ShouldBe("serializable");
    }

    [Fact]
    public async Task document_session_with_default_options_is_actually_serializable()
    {
        await theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(Bug5586Doc));

        await using var session = await theStore.OpenSerializableSessionAsync(new SessionOptions());

        (await readIsolationLevel(session)).ShouldBe("serializable");
    }

    /// <summary>
    /// The snapshot is the point of the isolation level, and it is only held from the moment the
    /// transaction opens. Before #5586 the transaction was never begun for default options, so a second
    /// read inside the "serializable" session saw somebody else's commit.
    /// </summary>
    [Fact]
    public async Task the_session_holds_one_snapshot_across_reads()
    {
        await theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(Bug5586Doc));

        var id = Guid.NewGuid();
        await using (var setup = theStore.LightweightSession())
        {
            setup.Store(new Bug5586Doc { Id = id, Value = 1 });
            await setup.SaveChangesAsync();
        }

        await using var session = await theStore.QuerySerializableSessionAsync(new SessionOptions());

        (await session.LoadAsync<Bug5586Doc>(id))!.Value.ShouldBe(1);

        await using (var other = theStore.LightweightSession())
        {
            other.Store(new Bug5586Doc { Id = id, Value = 2 });
            await other.SaveChangesAsync();
        }

        (await session.LoadAsync<Bug5586Doc>(id))!.Value
            .ShouldBe(1, "a serializable session must keep seeing its own snapshot");
    }

    /// <summary>
    /// An explicitly-set lower level is overridden rather than honoured. Passing ReadCommitted to a method
    /// called "Serializable" is a contradiction, and the non-nullable default means it cannot be told apart
    /// from not setting one at all.
    /// </summary>
    [Fact]
    public async Task an_explicit_lower_isolation_level_does_not_win()
    {
        await using var session = await theStore.QuerySerializableSessionAsync(
            new SessionOptions { IsolationLevel = IsolationLevel.ReadCommitted });

        (await readIsolationLevel(session)).ShouldBe("serializable");
    }

    private static async Task<string> readIsolationLevel(IQuerySession session)
    {
        var results = await session.AdvancedSql.QueryAsync<string>(
            "select current_setting('transaction_isolation')", default);
        return results[0];
    }

    public Bug_5586_async_serializable_sessions()
    {
        StoreOptions(opts => opts.Schema.For<Bug5586Doc>());
    }
}
