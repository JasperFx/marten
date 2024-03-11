using System;
using System.Linq;
using System.Threading;
using Marten.Metadata;
using Marten.Testing.Harness;
using Newtonsoft.Json;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Reading;

public class advanced_sql_query: IntegrationContext
{
    public advanced_sql_query(DefaultStoreFixture fixture): base(fixture)
    {
    }

    [Fact]
    public async void can_query_scalar()
    {
        using var session = theStore.LightweightSession();
        session.Store(new DocWithMeta { Id = 1, Name = "Max" });
        await session.SaveChangesAsync();
        var name = (await session.AdvancedSqlQueryAsync<string>(
            "select data ->> 'Name' from mt_doc_advanced_sql_query_docwithmeta LIMIT 1",
            CancellationToken.None)).First();
        name.ShouldBe("Max");
    }

    [Fact]
    public async void can_query_documents()
    {
        using var session = theStore.LightweightSession();
        session.Store(new DocWithoutMeta { Id = 1, Name = "Max" });
        session.Store(new DocWithoutMeta { Id = 2, Name = "Anne" });
        await session.SaveChangesAsync();
        var docs = await session.AdvancedSqlQueryAsync<DocWithoutMeta>(
            "select id, data from mt_doc_advanced_sql_query_docwithoutmeta order by data ->> 'Name'",
            CancellationToken.None);
        docs.Count.ShouldBe(2);
        docs[0].Name.ShouldBe("Anne");
        docs[1].Name.ShouldBe("Max");
    }

    [Fact]
    public async void can_query_documents_and_will_set_metadata_on_result_documents()
    {
        using var session = theStore.LightweightSession();
        session.Store(new DocWithMeta { Id = 1, Name = "Max" });
        await session.SaveChangesAsync();
        var doc = (await session.AdvancedSqlQueryAsync<DocWithMeta>(
            "select id, data, mt_version from mt_doc_advanced_sql_query_docwithmeta where data ->> 'Name' = 'Max'",
            CancellationToken.None)).First();
        doc.Id.ShouldBe(1);
        doc.Name.ShouldBe("Max");
        doc.Version.ShouldNotBe(Guid.Empty);
    }

    public class DocWithoutMeta
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    public class DocWithMeta: IVersioned
    {
        public int Id { get; set; }
        public string Name { get; set; }
        [JsonIgnore]
        public Guid Version { get; set; }
    }
}
