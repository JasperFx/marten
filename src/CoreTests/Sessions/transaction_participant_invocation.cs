using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Transactions;
using Marten;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace CoreTests.Sessions;

/// <summary>
/// Every connection lifetime is handed the session's transaction participants when its batch pages
/// execute. A participant exists to flush work belonging to another system, EF Core being the only
/// production implementation, into the same transaction Marten is about to commit. A lifetime that
/// accepts participants and does not invoke them therefore drops that work with no error.
/// </summary>
public class transaction_participant_invocation: BugIntegrationContext
{
    [Fact]
    public async Task participant_is_invoked_on_an_ordinary_session()
    {
        StoreOptions(opts => opts.RegisterDocumentType<Target>());
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var participant = new RecordingParticipant();

        await using var session = theStore.LightweightSession();
        ((ITransactionParticipantRegistrar)session).AddTransactionParticipant(participant);
        session.Store(Target.Random());
        await session.SaveChangesAsync();

        // The control. If this ever fails, the test below is proving nothing, because it would be
        // asserting a behaviour that does not exist on any lifetime.
        participant.Invocations.ShouldBe(1);
    }

    [Fact]
    public async Task participant_is_invoked_when_enlisted_in_an_ambient_transaction()
    {
        StoreOptions(opts => opts.RegisterDocumentType<Target>());
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var participant = new RecordingParticipant();

        using var scope = new TransactionScope(
            TransactionScopeOption.Required,
            new TransactionOptions(),
            TransactionScopeAsyncFlowOption.Enabled);

        await using (var session = theStore.LightweightSession(SessionOptions.ForCurrentTransaction()))
        {
            // AmbientTransactionLifetime, per SessionOptions.buildConnectionLifetime: DotNetTransaction
            // is set and is tested before the Connection branch.
            session.Connection.ShouldNotBeNull();

            ((ITransactionParticipantRegistrar)session).AddTransactionParticipant(participant);
            session.Store(Target.Random());
            await session.SaveChangesAsync();
        }

        scope.Complete();

        participant.Invocations.ShouldBe(1);
    }

    internal class RecordingParticipant: ITransactionParticipant
    {
        public int Invocations { get; private set; }

        public List<bool> TransactionWasNull { get; } = new();

        public Task BeforeCommitAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
            CancellationToken token)
        {
            Invocations++;

            // Recorded rather than asserted. Under an ambient System.Transactions scope the
            // connection is enlisted and there is no NpgsqlTransaction to hand over, which is the
            // design question this test exposes rather than answers.
            TransactionWasNull.Add(transaction == null);

            return Task.CompletedTask;
        }
    }
}
