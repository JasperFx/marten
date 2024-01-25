
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
        // Accessing the session.Connection object will quietly open
        // a "sticky" connection for the session
        var openCount = await session.Connection

            // This is using a helper extension method from Weasel
            .CreateCommand("select count(*) from tasks where status = 'open'")
            .ExecuteScalarAsync();
    }

    #endregion


    #region sample_explicit_transactions

    public static async Task explicit_transactions(IDocumentSession session)
    {
        // If in synchronous code, but don't mix this in real async code!!!!
        session.BeginTransaction();

        // Favor this within async code
        await session.BeginTransactionAsync(CancellationToken.None);
    }

    #endregion
}
