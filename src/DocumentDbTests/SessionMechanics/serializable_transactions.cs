using System;
using System.Data;
using System.Threading.Tasks;
using Marten;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
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
        await using var session = await theStore.QuerySerializableSessionAsync(new SessionOptions { Timeout = 1 });
        await using var cmd = (await session.GetConnectionAsync()).CreateCommand("delete from mt_doc_user");
        var e = await Should.ThrowAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());

        // ERROR: cannot execute DELETE in a read-only transaction
        // read_only_sql_transaction
        Assert.Equal("25006", e.SqlState);
    }

    public static TheoryData<string> SynchronousSerializableFactories => new()
    {
        nameof(IDocumentStore.LightweightSession),
        nameof(IDocumentStore.IdentitySession),
        nameof(IDocumentStore.DirtyTrackedSession),
        "LightweightSession(tenantId)",
        nameof(IDocumentStore.OpenSession),
        nameof(IDocumentStore.QuerySession)
    };

    // #5861: a Serializable session opens its connection and begins its transaction as it is created, which a
    // synchronous factory can no longer do without blocking. It refuses, and names the async factories.
    [Theory]
    [MemberData(nameof(SynchronousSerializableFactories))]
    public void synchronous_factories_refuse_a_serializable_session(string factory)
    {
        Action open = factory switch
        {
            nameof(IDocumentStore.LightweightSession) => () => theStore.LightweightSession(IsolationLevel.Serializable),
            nameof(IDocumentStore.IdentitySession) => () => theStore.IdentitySession(IsolationLevel.Serializable),
            nameof(IDocumentStore.DirtyTrackedSession) => () => theStore.DirtyTrackedSession(IsolationLevel.Serializable),
            "LightweightSession(tenantId)" => () => theStore.LightweightSession("*DEFAULT*", IsolationLevel.Serializable),
            nameof(IDocumentStore.OpenSession) => () =>
                theStore.OpenSession(new SessionOptions { IsolationLevel = IsolationLevel.Serializable }),
            _ => () => theStore.QuerySession(new SessionOptions { IsolationLevel = IsolationLevel.Serializable })
        };

        var ex = Should.Throw<NotSupportedException>(open);
        ex.Message.ShouldStartWith("Synchronous data access is not supported in Marten 10");
        ex.Message.ShouldContain("LightweightSerializableSessionAsync()");
        ex.Message.ShouldContain("QuerySerializableSessionAsync()");
    }

    [Fact]
    public async Task synchronous_factories_still_honor_other_isolation_levels()
    {
        await using var session = theStore.LightweightSession(IsolationLevel.RepeatableRead);
        session.Store(new User());
        await session.SaveChangesAsync();
    }

    public serializable_transactions(DefaultStoreFixture fixture) : base(fixture)
    {
    }
}
