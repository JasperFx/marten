using System.Linq;
using System.Threading.Tasks;
using System.Transactions;
using Marten;
using Marten.Exceptions;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Xunit;

namespace DocumentDbTests.Writing;

public class bulk_loading_Tests : OneOffConfigurationsContext, IAsyncLifetime
{
    [Fact]
    public async Task load_with_ignore_duplicates()
    {
        var data1 = Target.GenerateRandomData(100).ToArray();

        await theStore.BulkInsertAsync(data1);

        var data2 = Target.GenerateRandomData(50).ToArray();

        // Rigging up data2 so 5 of its values would be getting lost
        for (var i = 0; i < 5; i++)
        {
            data2[i].Id = data1[i].Id;
            data2[i].Number = -1;
        }

        await theStore.BulkInsertAsync(data2, BulkInsertMode.IgnoreDuplicates);

        using var session = theStore.QuerySession();
        session.Query<Target>().Count().ShouldBe(data1.Length + data2.Length - 5);

        for (var i = 0; i < 5; i++)
        {
            (await session.LoadAsync<Target>(data1[i].Id)).Number.ShouldBeGreaterThanOrEqualTo(0);
        }
    }

    [Fact]
    public async Task load_with_multiple_batches()
    {
        var data = Target.GenerateRandomData(100).ToArray();

        await theStore.BulkInsertAsync(data, batchSize: 15);

        theSession.Query<Target>().Count().ShouldBe(data.Length);

        (await theSession.LoadAsync<Target>(data[0].Id)).ShouldNotBeNull();
    }

    [Fact]
    public async Task load_with_overwrite_duplicates()
    {
        var data1 = Target.GenerateRandomData(100).ToArray();

        await theStore.BulkInsertAsync(data1);

        var data2 = Target.GenerateRandomData(50).ToArray();

        // Rigging up data2 so 5 of its values would be getting lost
        for (var i = 0; i < 5; i++)
        {
            data2[i].Id = data1[i].Id;
            data2[i].Number = -1;
        }

        await theStore.BulkInsertAsync(data2, BulkInsertMode.OverwriteExisting);

        using var session = theStore.QuerySession();
        session.Query<Target>().Count().ShouldBe(data1.Length + data2.Length - 5);

        // Values were overwritten
        for (var i = 0; i < 5; i++)
        {
            (await session.LoadAsync<Target>(data1[i].Id)).Number.ShouldBe(-1);
        }

        var count = session.Connection.CreateCommand()
            .Sql($"select count(*) from {SchemaName}.mt_doc_target where mt_last_modified is null")
            .ExecuteScalar();

        count.ShouldBe(0);
    }

    [Fact]
    public async Task load_with_small_batch()
    {
        #region sample_using_bulk_insert
        // This is just creating some randomized
        // document data
        var data = Target.GenerateRandomData(100).ToArray();

        // Load all of these into a Marten-ized database
        await theStore.BulkInsertAsync(data, batchSize: 500);

        // And just checking that the data is actually there;)
        theSession.Query<Target>().Count().ShouldBe(data.Length);
        #endregion

        (await theSession.LoadAsync<Target>(data[0].Id)).ShouldNotBeNull();
    }

    [Fact]
    public async Task load_with_small_batch_and_duplicated_data_field()
    {
        StoreOptions(_ =>
        {
            _.Schema.For<Target>().Duplicate(x => x.Date);
        });

        var data = Target.GenerateRandomData(100).ToArray();

        await theStore.BulkInsertAsync(data);

        theSession.Query<Target>().Count().ShouldBe(data.Length);

        theSession.Query<Target>().Any(x => x.Date == data[0].Date)
            .ShouldBeTrue();
    }

    [Fact]
    public async Task load_with_small_batch_and_duplicated_fields()
    {
        StoreOptions(_ => { _.Schema.For<Target>().Duplicate(x => x.String); });

        var data = Target.GenerateRandomData(100).ToArray();

        await theStore.BulkInsertAsync(data);

        theSession.Query<Target>().Count().ShouldBe(data.Length);

        theSession.Query<Target>().Any(x => x.String == data[0].String)
            .ShouldBeTrue();
    }

    [Fact]
    public async Task load_with_small_batch_and_ignore_duplicates_smoke_test()
    {
        #region sample_bulk_insert_with_IgnoreDuplicates
        var data = Target.GenerateRandomData(100).ToArray();

        await theStore.BulkInsertAsync(data, BulkInsertMode.IgnoreDuplicates);
        #endregion

        theSession.Query<Target>().Count().ShouldBe(data.Length);

        (await theSession.LoadAsync<Target>(data[0].Id)).ShouldNotBeNull();

        var count = theSession.Connection.CreateCommand()
            .Sql($"select count(*) from {SchemaName}.mt_doc_target where mt_last_modified is null")
            .ExecuteScalar();

        count.ShouldBe(0);
    }

    [Fact]
    public async Task load_with_small_batch_and_overwrites_smoke_test()
    {
        #region sample_bulk_insert_with_OverwriteExisting
        var data = Target.GenerateRandomData(100).ToArray();

        await theStore.BulkInsertAsync(data, BulkInsertMode.OverwriteExisting);
        #endregion

        theSession.Query<Target>().Count().ShouldBe(data.Length);

        (await theSession.LoadAsync<Target>(data[0].Id)).ShouldNotBeNull();
    }

    [Fact]
    public async Task load_with_ignore_duplicates_async()
    {
        var data1 = Target.GenerateRandomData(100).ToArray();

        await theStore.BulkInsertAsync(data1);

        var data2 = Target.GenerateRandomData(50).ToArray();

        // Rigging up data2 so 5 of its values would be getting lost
        for (var i = 0; i < 5; i++)
        {
            data2[i].Id = data1[i].Id;
            data2[i].Number = -1;
        }

        await theStore.BulkInsertAsync(data2, BulkInsertMode.IgnoreDuplicates);

        await using var session = theStore.QuerySession();
        session.Query<Target>().Count().ShouldBe(data1.Length + data2.Length - 5);

        for (var i = 0; i < 5; i++)
        {
            (await session.LoadAsync<Target>(data1[i].Id)).Number.ShouldBeGreaterThanOrEqualTo(0);
        }
    }

    [Fact]
    public async Task load_with_multiple_batches_async()
    {
        var data = Target.GenerateRandomData(100).ToArray();

        await theStore.BulkInsertAsync(data, batchSize: 15);

        theSession.Query<Target>().Count().ShouldBe(data.Length);

        (await theSession.LoadAsync<Target>(data[0].Id)).ShouldNotBeNull();
    }

    [Fact]
    public async Task load_with_overwrite_duplicates_async()
    {
        var data1 = Target.GenerateRandomData(100).ToArray();

        await theStore.BulkInsertAsync(data1);

        var data2 = Target.GenerateRandomData(50).ToArray();

        // Rigging up data2 so 5 of its values would be getting lost
        for (var i = 0; i < 5; i++)
        {
            data2[i].Id = data1[i].Id;
            data2[i].Number = -1;
        }

        await theStore.BulkInsertAsync(data2, BulkInsertMode.OverwriteExisting);

        await using var session = theStore.QuerySession();
        session.Query<Target>().Count().ShouldBe(data1.Length + data2.Length - 5);

        // Values were overwritten
        for (var i = 0; i < 5; i++)
        {
            (await session.LoadAsync<Target>(data1[i].Id)).Number.ShouldBe(-1);
        }

        var count = session.Connection.CreateCommand()
            .Sql($"select count(*) from {SchemaName}.mt_doc_target where mt_last_modified is null")
            .ExecuteScalar();

        count.ShouldBe(0);
    }

    [Fact]
    public async Task load_with_small_batch_async()
    {
        #region sample_using_bulk_insert_async
        // This is just creating some randomized
        // document data
        var data = Target.GenerateRandomData(100).ToArray();

        // Load all of these into a Marten-ized database
        await theStore.BulkInsertAsync(data, batchSize: 500);

        // And just checking that the data is actually there;)
        theSession.Query<Target>().Count().ShouldBe(data.Length);
        #endregion

        (await theSession.LoadAsync<Target>(data[0].Id)).ShouldNotBeNull();
    }

    [Fact]
    public async Task load_across_multiple_tenants_async()
    {
        StoreOptions(opts =>
        {
            opts.Policies.AllDocumentsAreMultiTenanted();
        });

        var data = Target.GenerateRandomData(100).ToArray();

        var tenant1 = "tenant_1";
        var tenant2 = "tenant_2";

        await theStore.BulkInsertAsync(tenant1, data, BulkInsertMode.OverwriteExisting);
        await theStore.BulkInsertAsync(tenant2, data, BulkInsertMode.OverwriteExisting);

        var tenant1Session = theStore.QuerySession(tenant1);
        var tenant2Session = theStore.QuerySession(tenant2);

        theSession.Query<Target>().Where(x => x.AnyTenant()).Count().ShouldBe(data.Length * 2);
        tenant1Session.Query<Target>().Count().ShouldBe(data.Length);
        tenant2Session.Query<Target>().Count().ShouldBe(data.Length);

        (await tenant1Session.LoadAsync<Target>(data[0].Id)).ShouldNotBeNull();
        (await tenant2Session.LoadAsync<Target>(data[0].Id)).ShouldNotBeNull();
    }

    [Fact]
    public async Task load_with_small_batch_and_duplicated_data_field_async()
    {
        StoreOptions(_ =>
        {
            _.Schema.For<Target>().Duplicate(x => x.Date);
        });

        var data = Target.GenerateRandomData(100).ToArray();

        await theStore.BulkInsertAsync(data);

        theSession.Query<Target>().Count().ShouldBe(data.Length);

        var cmd = theSession.Query<Target>().Where(x => x.Date == data[0].Date).ToCommand();

        theSession.Query<Target>().Any(x => x.Date == data[0].Date)
            .ShouldBeTrue();
    }

    [Fact]
    public async Task load_with_small_batch_and_duplicated_fields_async()
    {
        StoreOptions(_ => { _.Schema.For<Target>().Duplicate(x => x.String); });

        var data = Target.GenerateRandomData(100).ToArray();

        await theStore.BulkInsertAsync(data);

        theSession.Query<Target>().Count().ShouldBe(data.Length);

        theSession.Query<Target>().Any(x => x.String == data[0].String)
            .ShouldBeTrue();
    }

    internal async Task BulkInsertModeSamples()
    {
        #region sample_BulkInsertMode_usages

        // Just say we have an array of documents we want to bulk insert
        var data = Target.GenerateRandomData(100).ToArray();

        using var store = DocumentStore.For("some connection string");

        // Discard any documents that match the identity of an existing document
        // in the database
        await store.BulkInsertDocumentsAsync(data, BulkInsertMode.IgnoreDuplicates);

        // This is the default mode, the bulk insert will fail if any duplicate
        // identities with existing data or within the data set being loaded are detected
        await store.BulkInsertDocumentsAsync(data, BulkInsertMode.InsertsOnly);

        // Overwrite any existing documents with the same identity as the documents
        // being loaded
        await store.BulkInsertDocumentsAsync(data, BulkInsertMode.OverwriteExisting);

        #endregion
    }

    internal async Task MultiTenancySample()
    {
        #region sample_MultiTenancyWithBulkInsert

        // Just say we have an array of documents we want to bulk insert
        var data = Target.GenerateRandomData(100).ToArray();

        using var store = DocumentStore.For(opts =>
        {
            opts.Connection("some connection string");
            opts.Policies.AllDocumentsAreMultiTenanted();
        });

        // If multi-tenanted
        await store.BulkInsertDocumentsAsync("a tenant id", data);

        #endregion
    }

    [Fact]
    public async Task load_with_small_batch_and_ignore_duplicates_smoke_test_async()
    {
        #region sample_bulk_insert_async_with_IgnoreDuplicates
        var data = Target.GenerateRandomData(100).ToArray();

        await theStore.BulkInsertAsync(data, BulkInsertMode.IgnoreDuplicates);
        #endregion

        theSession.Query<Target>().Count().ShouldBe(data.Length);

        (await theSession.LoadAsync<Target>(data[0].Id)).ShouldNotBeNull();

        var count = theSession.Connection.CreateCommand()
            .Sql($"select count(*) from {SchemaName}.mt_doc_target where mt_last_modified is null")
            .ExecuteScalar();

        count.ShouldBe(0);
    }

    [Fact]
    public async Task load_with_small_batch_and_overwrites_smoke_test_async()
    {
        #region sample_bulk_insert_async_with_OverwriteExisting
        var data = Target.GenerateRandomData(100).ToArray();

        await theStore.BulkInsertAsync(data, BulkInsertMode.OverwriteExisting);
        #endregion

        theSession.Query<Target>().Count().ShouldBe(data.Length);

        (await theSession.LoadAsync<Target>(data[0].Id)).ShouldNotBeNull();
    }

    [Fact]
    public async Task store_multiple_types_of_documents_at_one_time()
    {
        var user1 = new User();
        var user2 = new User();
        var issue1 = new Issue();
        var issue2 = new Issue();
        var company1 = new Company();
        var company2 = new Company();

        theSession.Store<object>(user1, user2, issue1, issue2, company1, company2);
        await theSession.SaveChangesAsync();

        using (var querying = theStore.QuerySession())
        {
            querying.Query<User>().Count().ShouldBe(2);
            querying.Query<Issue>().Count().ShouldBe(2);
            querying.Query<Company>().Count().ShouldBe(2);
        }
    }

    [Fact]
    public async Task store_multiple_types_of_documents_at_one_time_by_StoreObjects()
    {
        var user1 = new User();
        var user2 = new User();
        var issue1 = new Issue();
        var issue2 = new Issue();
        var company1 = new Company();
        var company2 = new Company();

        var documents = new object[] { user1, user2, issue1, issue2, company1, company2};
        theSession.StoreObjects(documents);
        await theSession.SaveChangesAsync();

        using (var querying = theStore.QuerySession())
        {
            querying.Query<User>().Count().ShouldBe(2);
            querying.Query<Issue>().Count().ShouldBe(2);
            querying.Query<Company>().Count().ShouldBe(2);
        }
    }

    [Fact]
    public async Task can_bulk_insert_mixed_list_of_objects()
    {
        var user1 = new User();
        var user2 = new User();
        var issue1 = new Issue();
        var issue2 = new Issue();
        var company1 = new Company();
        var company2 = new Company();

        var documents = new object[] { user1, user2, issue1, issue2, company1, company2 };

        await theStore.BulkInsertAsync(documents);

        using (var querying = theStore.QuerySession())
        {
            querying.Query<User>().Count().ShouldBe(2);
            querying.Query<Issue>().Count().ShouldBe(2);
            querying.Query<Company>().Count().ShouldBe(2);
        }
    }

    [Fact]
    public async Task can_bulk_insert_mixed_list_of_objects_by_objects()
    {
        var user1 = new User();
        var user2 = new User();
        var issue1 = new Issue();
        var issue2 = new Issue();
        var company1 = new Company();
        var company2 = new Company();

        var documents = new object[] { user1, user2, issue1, issue2, company1, company2 };

        await theStore.BulkInsertDocumentsAsync(documents);

        await using var querying = theStore.QuerySession();
        querying.Query<User>().Count().ShouldBe(2);
        querying.Query<Issue>().Count().ShouldBe(2);
        querying.Query<Company>().Count().ShouldBe(2);
    }

    [Fact]
    public async Task load_enlist_transaction()
    {
        var data = Target.GenerateRandomData(100).ToArray();

        using (var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
        {
            await theStore.BulkInsertEnlistTransactionAsync(data, Transaction.Current);
            scope.Complete();
        }

        using var session = theStore.QuerySession();
        session.Query<Target>().Count().ShouldBe(data.Length);
    }

    [Fact]
    public async Task load_enlist_transaction_no_commit()
    {
        var data = Target.GenerateRandomData(100).ToArray();

        using (var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
        {
            await theStore.BulkInsertEnlistTransactionAsync(data, Transaction.Current);
        }

        using var session = theStore.QuerySession();
        Should.Throw<MartenCommandException>(() => session.Query<Target>().Count());
    }

    [Fact]
    public async Task load_enlist_transaction_async()
    {
        var data = Target.GenerateRandomData(100).ToArray();

        using (var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
        {
            await theStore.BulkInsertEnlistTransactionAsync(data, Transaction.Current);
            scope.Complete();
        }

        await using var session = theStore.QuerySession();
        session.Query<Target>().Count().ShouldBe(data.Length);
    }

    [Fact]
    public async Task load_enlist_transaction_async_no_commit()
    {
        var data = Target.GenerateRandomData(100).ToArray();

        using (var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
        {
            await theStore.BulkInsertEnlistTransactionAsync(data, Transaction.Current);
        }

        await using var session = theStore.QuerySession();
        Should.Throw<MartenCommandException>(() => session.Query<Target>().Count());
    }

    [Fact]
    public async Task can_bulk_insert_mixed_list_of_objects_enlist_transaction()
    {
        var user1 = new User();
        var user2 = new User();
        var issue1 = new Issue();
        var issue2 = new Issue();
        var company1 = new Company();
        var company2 = new Company();

        var documents = new object[] { user1, user2, issue1, issue2, company1, company2 };

        using (var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
        {
            await theStore.BulkInsertEnlistTransactionAsync(documents, Transaction.Current);
            scope.Complete();
        }

        using (var querying = theStore.QuerySession())
        {
            querying.Query<User>().Count().ShouldBe(2);
            querying.Query<Issue>().Count().ShouldBe(2);
            querying.Query<Company>().Count().ShouldBe(2);
        }
    }

    [Fact]
    public async Task can_bulk_insert_mixed_list_of_objects_enlist_transaction_async()
    {
        var user1 = new User();
        var user2 = new User();
        var issue1 = new Issue();
        var issue2 = new Issue();
        var company1 = new Company();
        var company2 = new Company();

        var documents = new object[] { user1, user2, issue1, issue2, company1, company2 };

        using (var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
        {
            await theStore.BulkInsertEnlistTransactionAsync(documents, Transaction.Current);
            scope.Complete();
        }

        await using (var querying = theStore.QuerySession())
        {
            querying.Query<User>().Count().ShouldBe(2);
            querying.Query<Issue>().Count().ShouldBe(2);
            querying.Query<Company>().Count().ShouldBe(2);
        }
    }

    [Fact]
    public async Task can_bulk_insert_soft_deletable_documents_when_using_overwrite_mode()
    {
        StoreOptions(x => x.Schema.For<User>().SoftDeletedWithIndex());

        var doc1 = new User();
        var doc2 = new User();

        var documents = new object[] { doc1, doc2 };

        await theStore.BulkInsertAsync(documents, BulkInsertMode.OverwriteExisting);

        await using (var querying = theStore.QuerySession())
        {
            querying.Query<User>().Count().ShouldBe(2);
        }
    }

    public Task InitializeAsync()
    {
        return theStore.Advanced.Clean.DeleteAllDocumentsAsync();
    }

    public Task DisposeAsync()
    {
        Dispose();
        return Task.CompletedTask;
    }
}
