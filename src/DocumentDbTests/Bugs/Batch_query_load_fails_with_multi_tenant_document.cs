using System;
using System.Threading.Tasks;
using JasperFx;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;

namespace DocumentDbTests.Bugs;

public class Batch_query_load_fails_with_multi_tenant_document: BugIntegrationContext
{
    [Fact]
    public async Task load_with_batch_query_should_work_for_multi_tenanted_document()
    {
        using var documentStore = SeparateStore(x =>
        {
            x.AutoCreateSchemaObjects = AutoCreate.All;
            x.Schema.For<User>().MultiTenanted();
        });

        await documentStore.Advanced.Clean.CompletelyRemoveAllAsync();
        await documentStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync(AutoCreate.All);

        var testDocument = new User { Id = Guid.NewGuid(), UserName = "Test name" };

        await using var tenantASession = documentStore.LightweightSession("tenant_a");
        tenantASession.Insert(testDocument);
        await tenantASession.SaveChangesAsync();

        await using var tenantAQuerySession = documentStore.LightweightSession("tenant_a");

        var batchQuery = tenantAQuerySession.CreateBatchQuery();
        var testDocumentWithBatchLoad = batchQuery.Load<User>(testDocument.Id);
        await batchQuery.Execute();

        testDocumentWithBatchLoad.Result.ShouldNotBeNull();
        testDocumentWithBatchLoad.Result.UserName.ShouldBe(testDocument.UserName);
    }

    [Fact]
    public async Task load_with_batch_query_should_not_return_document_from_other_tenant()
    {
        using var documentStore = SeparateStore(x =>
        {
            x.AutoCreateSchemaObjects = AutoCreate.All;
            x.Schema.For<User>().MultiTenanted();
        });

        await documentStore.Advanced.Clean.CompletelyRemoveAllAsync();
        await documentStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync(AutoCreate.All);

        var testDocument = new User { Id = Guid.NewGuid(), UserName = "Test name" };

        await using var tenantASession = documentStore.LightweightSession("tenant_a");
        tenantASession.Insert(testDocument);
        await tenantASession.SaveChangesAsync();

        await using var tenantBQuerySession = documentStore.LightweightSession("tenant_b");

        var batchQuery = tenantBQuerySession.CreateBatchQuery();
        var testDocumentWithBatchLoad = batchQuery.Load<User>(testDocument.Id);
        await batchQuery.Execute();

        testDocumentWithBatchLoad.Result.ShouldBeNull();
    }
}
