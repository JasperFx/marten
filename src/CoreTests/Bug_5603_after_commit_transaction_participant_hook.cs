#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Marten;
using Marten.Services;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace CoreTests;

public class Bug5603Doc
{
    public Guid Id { get; set; }
    public int Value { get; set; }
}

/// <summary>
/// #5603 added <see cref="ITransactionParticipant.AfterCommitAsync" />, because
/// <c>BeforeCommitAsync</c> runs inside the block Marten's write resilience pipeline retries and so
/// could never be the place a participant learns its work became durable.
///
/// <para>
/// These tests cover the hook itself rather than the EF Core participant that needed it. The
/// distinction that matters is the one a participant holding provisional state depends on: called
/// after a commit, and NOT called when there was no commit. It is also asserted on each lifetime
/// that performs its own commit, because <c>BeforeCommitAsync</c> has three separate call sites and
/// a hook added to only one of them would be worse than no hook at all — a participant would accept
/// its state on some sessions and not others.
/// </para>
/// </summary>
public class Bug_5603_after_commit_transaction_participant_hook: OneOffConfigurationsContext
{
    /// <summary>
    /// Records the order of the calls, not just their counts: the whole point of the hook is that it
    /// lands after the commit, and a count alone would not notice it firing first.
    /// </summary>
    private sealed class ProbeParticipant: ITransactionParticipant
    {
        public List<string> Calls { get; } = new();

        public Task BeforeCommitAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
            CancellationToken token)
        {
            Calls.Add("before");
            return Task.CompletedTask;
        }

        public Task AfterCommitAsync(CancellationToken token)
        {
            Calls.Add("after");
            return Task.CompletedTask;
        }
    }

    /// <summary>A participant that does not implement the new member at all.</summary>
    private sealed class LegacyParticipant: ITransactionParticipant
    {
        public int Befores { get; private set; }

        public Task BeforeCommitAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
            CancellationToken token)
        {
            Befores++;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task fires_after_the_commit_on_the_default_lifetime()
    {
        await using var session = theStore.LightweightSession();
        var probe = new ProbeParticipant();
        ((ITransactionParticipantRegistrar)session).AddTransactionParticipant(probe);

        session.Store(new Bug5603Doc { Value = 1 });
        await session.SaveChangesAsync(TestContext.Current.CancellationToken);

        probe.Calls.ShouldBe(["before", "after"]);
    }

    [Fact]
    public async Task fires_after_the_commit_on_a_caller_supplied_transaction()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync(TestContext.Current.CancellationToken);
        await using var tx = await conn.BeginTransactionAsync(TestContext.Current.CancellationToken);

        await using var session = theStore.LightweightSession(SessionOptions.ForTransaction(tx));
        var probe = new ProbeParticipant();
        ((ITransactionParticipantRegistrar)session).AddTransactionParticipant(probe);

        session.Store(new Bug5603Doc { Value = 2 });
        await session.SaveChangesAsync(TestContext.Current.CancellationToken);

        probe.Calls.ShouldBe(["before", "after"]);
    }

    [Fact]
    public async Task fires_after_the_commit_on_a_sticky_connection()
    {
        // The third lifetime that commits for itself: TransactionalConnection, reached here through a
        // caller-supplied connection, and also what a sticky-lifetime or Serializable session gets.
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync(TestContext.Current.CancellationToken);

        await using var session = theStore.LightweightSession(SessionOptions.ForConnection(conn));
        var probe = new ProbeParticipant();
        ((ITransactionParticipantRegistrar)session).AddTransactionParticipant(probe);

        session.Store(new Bug5603Doc { Value = 4 });
        await session.SaveChangesAsync(TestContext.Current.CancellationToken);

        probe.Calls.ShouldBe(["before", "after"]);
    }

    [Fact]
    public async Task does_not_fire_when_the_save_fails()
    {
        // The case a participant holding provisional state actually depends on. If the hook fired
        // here it would accept changes that no transaction ever committed, which is the shape of the
        // bug #5603 was, only reversed.
        await using var session = theStore.LightweightSession();
        var probe = new ProbeParticipant();
        ((ITransactionParticipantRegistrar)session).AddTransactionParticipant(probe);

        session.QueueSqlCommand("insert into a_table_that_does_not_exist (id) values (1)");

        await Should.ThrowAsync<Exception>(async () =>
            await session.SaveChangesAsync(TestContext.Current.CancellationToken));

        probe.Calls.ShouldNotContain("after");
    }

    [Fact]
    public async Task a_participant_that_does_not_implement_the_new_member_still_works()
    {
        // The member is default-implemented so that adding it breaks no existing implementor. This
        // asserts the default is reachable rather than trusting that it compiles -- a participant
        // written against 9.46 is a plain ITransactionParticipant with one method.
        await using var session = theStore.LightweightSession();
        var legacy = new LegacyParticipant();
        ((ITransactionParticipantRegistrar)session).AddTransactionParticipant(legacy);

        session.Store(new Bug5603Doc { Value = 3 });
        await session.SaveChangesAsync(TestContext.Current.CancellationToken);

        legacy.Befores.ShouldBe(1);
    }
}
