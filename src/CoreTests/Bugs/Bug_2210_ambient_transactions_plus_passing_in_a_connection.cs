using System.Threading;
using System.Threading.Tasks;
using System.Transactions;
using Marten;
using Marten.Internal.Sessions;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;
using IsolationLevel = System.Data.IsolationLevel;

namespace CoreTests.Bugs;

public class Bug_2210_ambient_transactions_plus_passing_in_a_connection: BugIntegrationContext
{
    [Fact]
    public async Task do_not_blow_up()
    {
        StoreOptions(opts => opts.RegisterDocumentType<Target>());

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var transaction = new TransactionScope(
            TransactionScopeOption.Required,
            new TransactionOptions { },
            TransactionScopeAsyncFlowOption.Enabled);

        using (transaction)
        {
            /* READ
             * We open the connection here to ensure it enlists in the transaction scope.
             * If an error occurs, that means the connection is already open and the calling
             * code probably has a bug or something, or perhaps the code is sending through
             * the mediator more than once. If the latter is the case, we need to ask why.
             * If there is a valid reason for it, then perhaps this code needs to be re-worked
             * a bit to allow for activities to be sent through the mediator more than once,
             * but that should be a rare exception.
             * */
            var connection = new NpgsqlConnection(ConnectionSource.ConnectionString);
            connection.Open();


            var options = SessionOptions.ForConnection(connection).EnlistInAmbientTransactionScope();
            options.IsolationLevel = IsolationLevel.ReadCommitted;

            var martenConnection =
                await options.InitializeAsync(theStore, CommandRunnerMode.External, CancellationToken.None);
            var lifetime = martenConnection.ShouldBeOfType<AmbientTransactionLifetime>();
            lifetime.OwnsConnection.ShouldBeFalse();
            lifetime.Connection.ShouldBe(connection);

            await using var session = theStore.LightweightSession(options);

            session.Store(Target.Random());
            await session.SaveChangesAsync();

            transaction.Complete();
        }
    }
}
