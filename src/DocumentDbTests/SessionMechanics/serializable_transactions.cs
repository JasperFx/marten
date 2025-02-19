using System.Data;
using System.Threading.Tasks;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Npgsql;
using Weasel.Core;
using Xunit;

namespace DocumentDbTests.SessionMechanics;

public class serializable_transactions: IntegrationContext
{
    [Fact]
    public async Task should_respect_isolationlevel_and_be_read_only_transaction_when_serializable_isolation()
    {
        var user = new User();

        await theStore.BulkInsertDocumentsAsync(new [] { user });
        using var session = theStore.QuerySession(new SessionOptions() { IsolationLevel = IsolationLevel.Serializable, Timeout = 1 });
        using var cmd = session.Connection.CreateCommand("delete from mt_doc_user");
        var e = Assert.Throws<PostgresException>(() => cmd.ExecuteNonQuery());

        // ERROR: cannot execute DELETE in a read-only transaction
        // read_only_sql_transaction
        Assert.Equal("25006", e.SqlState);
    }

    public serializable_transactions(DefaultStoreFixture fixture) : base(fixture)
    {
    }
}
