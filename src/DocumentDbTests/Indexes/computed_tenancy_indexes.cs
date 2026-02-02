using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using JasperFx;
using Marten;
using Marten.Schema;
using Marten.Storage.Metadata;
using Marten.Testing;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql.Tables;
using Xunit;

namespace DocumentDbTests.Indexes;

public class computed_tenancy_indexes: OneOffConfigurationsContext
{
    [Fact]
    public async Task create_index_that_includes_tenant_id()
    {
        StoreOptions(_ =>
        {
            _.Policies.AllDocumentsAreMultiTenantedWithPartitioning(c =>
            {
                c.ByHash(Enumerable.Range(0, 4).Select(i => $"h{i:000}").ToArray());
            });
            #region sample_tenancy-start_indexes_by_tenant_id
            _.Policies.ForAllDocuments(x =>
            {
                x.StartIndexesByTenantId = true;
            });
            _.Schema.For<User>()
                .StartIndexesByTenantId()
                .Index(x => x.UserName);
            #endregion
            _.Schema.For<Target>()
                .Index(x => x.Color);

        });

        var data = Target.GenerateRandomData(10).ToArray();
        await theStore.BulkInsertAsync(data);

        var table = await theStore.Tenancy.Default.Database.ExistingTableFor(typeof(Target));
        var index = table.IndexFor("mt_doc_target_idx_color");

        index.ToDDL(table).ShouldBe("CREATE INDEX mt_doc_target_idx_color ON computed_tenancy_indexes.mt_doc_target USING btree (tenant_id, CAST(data ->> 'Color' as integer));");
    }

    [Fact]
    public async Task create_multi_property_index_that_includes_tenant_id()
    {
        StoreOptions(_ =>
        {
            _.Policies.AllDocumentsAreMultiTenantedWithPartitioning(c =>
            {
                c.ByHash(Enumerable.Range(0, 4).Select(i => $"h{i:000}").ToArray());
            });
            _.Schema.For<Target>()
                .StartIndexesByTenantId()
                .Index([
                    x => x.UserId,
                    x => x.Flag
                ]);
        });

        var data = Target.GenerateRandomData(10).ToArray();
        await theStore.BulkInsertAsync(data);

        var table = await theStore.Tenancy.Default.Database.ExistingTableFor(typeof(Target));
        var index = table.IndexFor("mt_doc_target_idx_user_idflag");

        index.ToDDL(table).ShouldBe("CREATE INDEX mt_doc_target_idx_user_idflag ON computed_tenancy_indexes.mt_doc_target USING btree (tenant_id, CAST(data ->> 'UserId' as uuid), CAST(data ->> 'Flag' as boolean));");
    }
}
