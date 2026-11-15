
using System.Threading;
using System.Threading.Tasks;
using Weasel.Postgresql;
using Xunit;

namespace Marten.Testing.Examples;

public class ExplicitTransactions
{
    #region sample_using_session_connection_directly

    public static async Task using_session_connection(IQuerySession session)
    {
        // GetConnectionAsync() opens a "sticky" connection for the session
        // if it does not already have one. The session owns it, so don't dispose it
        var connection = await session.GetConnectionAsync();

        // This is using a helper extension method from Weasel
        var openCount = await connection
            .CreateCommand("select count(*) from tasks where status = 'open'")
            .ExecuteScalarAsync();
    }

    #endregion


    #region sample_explicit_transactions

    public static async Task explicit_transactions(IDocumentSession session)
    {
        // Opens the session's connection and begins its transaction. There is no
        // synchronous BeginTransaction() as of Marten 10
        await session.BeginTransactionAsync(CancellationToken.None);
    }

    #endregion
}
