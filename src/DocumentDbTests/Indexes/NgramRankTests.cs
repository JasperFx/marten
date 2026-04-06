using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Indexes;

public class NgramRankTests : OneOffConfigurationsContext
{
    [Fact]
    public async Task can_order_by_ngram_rank()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<SearchDocument>().NgramIndex(x => x.Content);
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        await using (var session = theStore.LightweightSession())
        {
            // Insert documents with varying relevance to "blue shoes"
            session.Store(new SearchDocument { Id = Guid.NewGuid(), Content = "blue shoes for running" });
            session.Store(new SearchDocument { Id = Guid.NewGuid(), Content = "red hat and blue scarf" });
            session.Store(new SearchDocument { Id = Guid.NewGuid(), Content = "blue shoes blue shoes blue shoes" });
            session.Store(new SearchDocument { Id = Guid.NewGuid(), Content = "green garden tools" });
            await session.SaveChangesAsync();
        }

        await using var querySession = theStore.QuerySession();

        // Search for "blue shoes" and order by relevance
        var results = await querySession.Query<SearchDocument>()
            .Where(x => x.Content.NgramSearch("blue shoes"))
            .OrderByNgramRank(x => x.Content, "blue shoes")
            .ToListAsync();

        // Should find documents containing "blue shoes" terms, ordered by relevance
        results.Count.ShouldBeGreaterThan(0);

        // All results should contain at least one of the search terms
        results.ShouldAllBe(r => r.Content.Contains("blue") || r.Content.Contains("shoes"));

        // The "green garden tools" document should NOT appear (doesn't match the ngram search)
        results.ShouldNotContain(r => r.Content.Contains("green garden"));
    }

    [Fact]
    public async Task order_by_ngram_rank_with_select()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<SearchDocument>().NgramIndex(x => x.Content);
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        await using (var session = theStore.LightweightSession())
        {
            session.Store(new SearchDocument { Id = Guid.NewGuid(), Content = "alpha beta gamma" });
            session.Store(new SearchDocument { Id = Guid.NewGuid(), Content = "alpha alpha alpha" });
            await session.SaveChangesAsync();
        }

        await using var querySession = theStore.QuerySession();

        var results = await querySession.Query<SearchDocument>()
            .Where(x => x.Content.NgramSearch("alpha"))
            .OrderByNgramRank(x => x.Content, "alpha")
            .Select(x => x.Content)
            .ToListAsync();

        results.Count.ShouldBe(2);
        // Both documents match, ordered by rank (highest first)
        results.ShouldContain("alpha alpha alpha");
        results.ShouldContain("alpha beta gamma");
    }
}

public class SearchDocument
{
    public Guid Id { get; set; }
    public string Content { get; set; } = "";
}
