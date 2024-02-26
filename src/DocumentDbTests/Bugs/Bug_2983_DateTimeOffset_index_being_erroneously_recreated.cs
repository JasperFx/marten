using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Marten.Storage;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Postgresql.Tables;
using Xunit;

namespace DocumentDbTests.Bugs;

public class Bug_2983_DateTimeOffset_index_being_erroneously_recreated : BugIntegrationContext
{
    [Fact]
    public async Task do_not_recreate_index_if_not_changed()
    {
        StoreOptions(sp =>
        {
            sp.Schema.For<Photo>()
                .Identity(c => c.Id)
                .Index(c => c.Date, c =>
                {
                    c.SortOrder = Weasel.Postgresql.Tables.SortOrder.Desc;
                    c.TableSpace = "pg_default";
                })
                .GinIndexJsonData();
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var store2 = SeparateStore(sp =>
        {
            sp.Schema.For<Photo>()
                .Identity(c => c.Id)
                .Index(c => c.Date, c =>
                {
                    c.SortOrder = Weasel.Postgresql.Tables.SortOrder.Desc;
                    c.TableSpace = "pg_default";
                })
                .GinIndexJsonData();
        });

        //await store2.Storage.Database.AssertDatabaseMatchesConfigurationAsync();

        var table = store2.Storage.Database.AllObjects().OfType<DocumentTable>().Single();

        foreach (var index in table.Indexes)
        {
            Debug.WriteLine(index.ToDDL(table));
        }
    }

    [Fact]
    public void how_about_DDL_comparison()
    {
        var generated = "CREATE INDEX mt_doc_photo_idx_date ON public.mt_doc_photo USING btree ((mt_immutable_timestamptz(data ->> 'Date')) DESC) TABLESPACE pg_default;";
        var database = "CREATE INDEX IF NOT EXISTS mt_doc_photo_idx_date ON public.mt_doc_photo USING btree (mt_immutable_timestamptz(data ->> 'Date'::text) DESC NULLS FIRST) TABLESPACE pg_default;";

        IndexDefinition.CanonicizeDdl(generated).ShouldBe(IndexDefinition.CanonicizeDdl(database));
    }
}

public class Photo
{
    public Guid Id { get; set; }
    public string? OriginalId { get; set; }
    public string Model { get; set; }
    public DateTimeOffset Date { get; set; }

    public string Url { get; set; }

}
