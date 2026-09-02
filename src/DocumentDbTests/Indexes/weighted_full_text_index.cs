using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Exceptions;
using Marten.Schema.Indexing.FullText;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables.Indexes;
using Xunit;

namespace DocumentDbTests.Indexes;

/// <summary>
/// #5298, index half. A plain <c>FullTextIndex</c> concatenates its members as TEXT and converts once,
/// so every match is equally relevant. Weighting cannot be expressed that way: <c>setweight</c> labels a
/// VECTOR, so each member is converted separately and the vectors are concatenated. That expression is a
/// tsvector at the top level, which is why it needs Weasel's <c>ForTsVector</c> rather than
/// <c>DocumentConfig</c> (weasel#541, shipped in 9.29.0).
/// </summary>
[Collection("OneOffs")]
public class weighted_full_text_index: OneOffConfigurationsContext
{
    public class Achievement
    {
        public Guid Id { get; set; }
        public string Title { get; set; }
        public string Tagline { get; set; }
        public string Description { get; set; }
    }

    private void ConfigureWeightedStore()
    {
        StoreOptions(opts => opts.Schema.For<Achievement>().WeightedFullTextIndex(idx => idx
            .Weighted(a => a.Title, TextSearchWeight.A)
            .Weighted(a => a.Tagline, TextSearchWeight.B)
            .Weighted(a => a.Description, TextSearchWeight.C)));
    }

    [Fact]
    public void the_index_is_built_over_a_pre_weighted_vector()
    {
        ConfigureWeightedStore();

        var index = theStore.Options.Storage.MappingFor(typeof(Achievement))
            .Indexes.OfType<FullTextIndexDefinition>().Single();

        index.TsVectorExpression.ShouldNotBeNull();

        // Each member converted separately and labelled, then the VECTORS concatenated.
        index.TsVectorExpression.ShouldContain("setweight(to_tsvector('english', coalesce(data ->> 'Title', '')), 'A')");
        index.TsVectorExpression.ShouldContain("setweight(to_tsvector('english', coalesce(data ->> 'Tagline', '')), 'B')");
        index.TsVectorExpression.ShouldContain("setweight(to_tsvector('english', coalesce(data ->> 'Description', '')), 'C')");
        index.TsVectorExpression.ShouldContain("||");

        // And crucially NOT wrapped in another to_tsvector, which would be a type error rather than a
        // weighted index. This is the whole reason weasel#541 existed.
        index.IndexedTsVector.ShouldNotStartWith("to_tsvector(");
    }

    /// <summary>
    /// The coalesce is load-bearing, not decoration: to_tsvector of NULL is NULL, and concatenating NULL
    /// into a vector annihilates the whole expression — so one absent member would silently empty the
    /// index row for that document rather than just omitting that field.
    /// </summary>
    [Fact]
    public async Task a_document_with_a_null_member_still_indexes_its_other_members()
    {
        ConfigureWeightedStore();

        var id = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.Store(new Achievement { Id = id, Title = "Dragonslayer", Tagline = null, Description = null });
            await session.SaveChangesAsync();
        }

        await using var query = theStore.QuerySession();
        var found = await query.Query<Achievement>().Where(x => x.PlainTextSearch("Dragonslayer")).ToListAsync();

        found.Select(x => x.Id).ShouldContain(id);
    }

    [Fact]
    public async Task the_index_is_actually_created_in_postgresql()
    {
        ConfigureWeightedStore();
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync(TestContext.Current.CancellationToken);

        await using var reader = await conn
            .CreateCommand(
                "select indexdef from pg_indexes where schemaname = :schema and tablename = 'mt_doc_weighted_full_text_index_achievement'")
            .With("schema", theStore.Options.DatabaseSchemaName, NpgsqlTypes.NpgsqlDbType.Varchar)
            .ExecuteReaderAsync(TestContext.Current.CancellationToken);

        var defs = new List<string>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken)) defs.Add(reader.GetString(0));

        defs.ShouldContain(d => d.Contains("setweight"));
    }

    /// <summary>
    /// A weighted index whose expression PostgreSQL hands back differently than we wrote it would read as
    /// drift on every single migration — recreating the index each time, which is the outage the whole
    /// feature is supposed to avoid. Weasel canonicalizes for this, and this asserts the composition
    /// actually holds end to end rather than trusting it.
    /// </summary>
    [Fact]
    public async Task a_weighted_index_round_trips_with_no_drift()
    {
        ConfigureWeightedStore();
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        await Should.NotThrowAsync(() => theStore.Storage.Database.AssertDatabaseMatchesConfigurationAsync());

        // ...and a second apply is a no-op rather than a drop-and-recreate.
        await Should.NotThrowAsync(() => theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync());
        await Should.NotThrowAsync(() => theStore.Storage.Database.AssertDatabaseMatchesConfigurationAsync());
    }

    /// <summary>
    /// One weight, or the same weight everywhere, ranks nothing — setweight only ever expresses a
    /// RELATIVE ordering. Refused at configuration time rather than emitting DDL that looks weighted and
    /// is not, because otherwise the failure surfaces as a ranked screen returning arbitrary order.
    /// </summary>
    [Fact]
    public void a_uniform_weighting_is_refused()
    {
        // MartenRegistry defers configuration into _builder.Alter, so the guard fires when the mapping
        // is built rather than when WeightedFullTextIndex() is called. Resolving the mapping is what
        // forces it -- asserting on the StoreOptions() call alone passes vacuously.
        Should.Throw<ArgumentOutOfRangeException>(() =>
        {
            var store = StoreOptions(opts => opts.Schema.For<Achievement>().WeightedFullTextIndex(idx => idx
                .Weighted(a => a.Title, TextSearchWeight.A)
                .Weighted(a => a.Description, TextSearchWeight.A)));

            store.Options.Storage.MappingFor(typeof(Achievement));
        });
    }

    [Fact]
    public void a_weighted_index_with_no_members_is_refused()
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
        {
            var store = StoreOptions(opts => opts.Schema.For<Achievement>().WeightedFullTextIndex(idx => { }));
            store.Options.Storage.MappingFor(typeof(Achievement));
        });
    }
}
