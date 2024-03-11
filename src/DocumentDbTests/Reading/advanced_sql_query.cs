using System;
using System.Collections.Generic;
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
        #region sample_advanced_sql_query_single_scalar
        var name = (await session.AdvancedSqlQueryAsync<string>(
            "select data ->> 'Name' from mt_doc_advanced_sql_query_docwithmeta limit 1",
            CancellationToken.None)).First();
        #endregion
        name.ShouldBe("Max");
    }

    [Fact]
    public async void can_query_multiple_scalars()
    {
        using var session = theStore.LightweightSession();
        #region sample_advanced_sql_query_multiple_scalars
        var (number,text, boolean) = (await session.AdvancedSqlQueryAsync<int, string, bool>(
            "select row(5), row('foo'), row(true) from (values(1)) as dummy",
            CancellationToken.None)).First();
        #endregion
        number.ShouldBe(5);
        text.ShouldBe("foo");
        boolean.ShouldBe(true);
    }

    [Fact]
    public async void can_query_non_document_classes_from_json()
    {
        using var session = theStore.LightweightSession();
        #region sample_advanced_sql_query_json_object
        var result = (await session.AdvancedSqlQueryAsync<Foo, Bar>(
            "select row(json_build_object('Name', 'foo')), row(json_build_object('Name', 'bar')) from (values(1)) as dummy",
            CancellationToken.None)).First();
        #endregion
        result.Item1.Name.ShouldBe("foo");
        result.Item2.Name.ShouldBe("bar");
    }

    [Fact]
    public async void can_query_documents()
    {
        using var session = theStore.LightweightSession();
        session.Store(new DocWithoutMeta { Id = 1, Name = "Max" });
        session.Store(new DocWithoutMeta { Id = 2, Name = "Anne" });
        await session.SaveChangesAsync();
        #region sample_advanced_sql_query_documents
        var docs = await session.AdvancedSqlQueryAsync<DocWithoutMeta>(
            "select id, data from mt_doc_advanced_sql_query_docwithoutmeta order by data ->> 'Name'",
            CancellationToken.None);
        #endregion
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
        #region sample_advanced_sql_query_documents_with_metadata
        var doc = (await session.AdvancedSqlQueryAsync<DocWithMeta>(
            "select id, data, mt_version from mt_doc_advanced_sql_query_docwithmeta where data ->> 'Name' = 'Max'",
            CancellationToken.None)).First();
        #endregion
        doc.Id.ShouldBe(1);
        doc.Name.ShouldBe("Max");
        doc.Version.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async void can_query_multiple_documents_and_scalar()
    {
        using var session = theStore.LightweightSession();
        #region sample_advanced_sql_query_related_documents_and_scalar
        session.Store(new DocWithMeta { Id = 1, Name = "Max" });
        session.Store(new DocDetailsWithMeta { Id = 1, Detail = "Likes bees" });
        session.Store(new DocWithMeta { Id = 2, Name = "Michael" });
        session.Store(new DocDetailsWithMeta { Id = 2, Detail = "Is a good chess player" });
        session.Store(new DocWithMeta { Id = 3, Name = "Anne" });
        session.Store(new DocDetailsWithMeta { Id = 3, Detail = "Hates soap operas" });
        session.Store(new DocWithMeta { Id = 4, Name = "Beatrix" });
        session.Store(new DocDetailsWithMeta { Id = 4, Detail = "Likes to cook" });
        await session.SaveChangesAsync();

        IReadOnlyList<(DocWithMeta doc, DocDetailsWithMeta detail, long totalResults)> results =
            await session.AdvancedSqlQueryAsync<DocWithMeta, DocDetailsWithMeta, long>(
                """
                select
                  row(a.id, a.data, a.mt_version),
                  row(b.id, b.data, b.mt_version),
                  row(count(*) over())
                from
                  mt_doc_advanced_sql_query_docwithmeta a
                left join
                  mt_doc_advanced_sql_query_docdetailswithmeta b on a.id = b.id
                where
                  (a.data ->> 'Id')::int > 1
                order by
                  a.data ->> 'Name'
                limit 2
                """,
                CancellationToken.None);

        results.Count.ShouldBe(2);
        results[0].totalResults.ShouldBe(3);
        results[0].doc.Name.ShouldBe("Anne");
        results[0].detail.Detail.ShouldBe("Hates soap operas");
        results[1].doc.Name.ShouldBe("Beatrix");
        results[1].detail.Detail.ShouldBe("Likes to cook");
        #endregion
    }

    [Fact]
    public void can_query_synchrounously()
    {
        using var session = theStore.LightweightSession();

        var singleResult  = session.AdvancedSqlQuery<int>("select 5 from (values(1)) as dummy").First();
        var tuple2Result = session.AdvancedSqlQuery<int, string>(
            "select row(5), row('foo')from (values(1)) as dummy").First();
        var tuple3Result = session.AdvancedSqlQuery<int, string, bool>(
            "select row(5), row('foo'), row(true) from (values(1)) as dummy").First();

        singleResult.ShouldBe(5);

        tuple2Result.Item1.ShouldBe(5);
        tuple2Result.Item2.ShouldBe("foo");

        tuple3Result.Item1.ShouldBe(5);
        tuple3Result.Item2.ShouldBe("foo");
        tuple3Result.Item3.ShouldBe(true);
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

    public class DocDetailsWithMeta: IVersioned
    {
        public int Id { get; set; }
        public string Detail { get; set; }
        [JsonIgnore]
        public Guid Version { get; set; }
    }

    public class Foo
    {
        public string Name { get; set; }
    }

    public class Bar
    {
        public string Name { get; set; }
    }
}
