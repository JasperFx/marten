using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Bugs;

public class Bug_3009_select_before_where: BugIntegrationContext
{
    [Fact]
    public async Task select_before_where_with_different_type()
    {
        var doc1 = new DocWithInner { Id = Guid.NewGuid(), Name = "one", Inner = new InnerDoc { Value = 10, Text = "low" } };
        var doc2 = new DocWithInner { Id = Guid.NewGuid(), Name = "two", Inner = new InnerDoc { Value = 50, Text = "mid" } };
        var doc3 = new DocWithInner { Id = Guid.NewGuid(), Name = "three", Inner = new InnerDoc { Value = 90, Text = "high" } };

        theSession.Store(doc1, doc2, doc3);
        await theSession.SaveChangesAsync();

        // Select().Where() - the problematic ordering from GH-3009
        var results = await theSession.Query<DocWithInner>()
            .Select(x => x.Inner)
            .Where(x => x.Value > 40)
            .ToListAsync();

        results.Count.ShouldBe(2);
        results.ShouldContain(x => x.Value == 50);
        results.ShouldContain(x => x.Value == 90);
    }

    [Fact]
    public async Task select_before_where_matches_where_before_select_different_type()
    {
        var doc1 = new DocWithInner { Id = Guid.NewGuid(), Name = "one", Inner = new InnerDoc { Value = 10, Text = "low" } };
        var doc2 = new DocWithInner { Id = Guid.NewGuid(), Name = "two", Inner = new InnerDoc { Value = 50, Text = "mid" } };
        var doc3 = new DocWithInner { Id = Guid.NewGuid(), Name = "three", Inner = new InnerDoc { Value = 90, Text = "high" } };

        theSession.Store(doc1, doc2, doc3);
        await theSession.SaveChangesAsync();

        // Normal order: Where().Select()
        var expected = await theSession.Query<DocWithInner>()
            .Where(x => x.Inner.Value > 40)
            .Select(x => x.Inner)
            .ToListAsync();

        // Reversed order: Select().Where()
        var actual = await theSession.Query<DocWithInner>()
            .Select(x => x.Inner)
            .Where(x => x.Value > 40)
            .ToListAsync();

        actual.Count.ShouldBe(expected.Count);
        actual.Select(x => x.Value).OrderBy(x => x)
            .ShouldBe(expected.Select(x => x.Value).OrderBy(x => x));
    }

    [Fact]
    public async Task select_before_where_with_same_type()
    {
        // Target.Inner is also of type Target, so this tests same-type Select hoisting
        var targets = Target.GenerateRandomData(50).ToArray();
        await theStore.BulkInsertAsync(targets);

        // Only targets where Inner is not null
        var targetsWithInner = targets.Where(x => x.Inner != null).ToArray();

        // Normal order
        var expected = await theSession.Query<Target>()
            .Where(x => x.Inner != null && x.Inner.Number > 0)
            .Select(x => x.Inner)
            .ToListAsync();

        // Reversed order
        var actual = await theSession.Query<Target>()
            .Select(x => x.Inner)
            .Where(x => x != null && x.Number > 0)
            .ToListAsync();

        actual.Count.ShouldBe(expected.Count);
    }

    [Fact]
    public async Task select_before_multiple_where_clauses()
    {
        var doc1 = new DocWithInner { Id = Guid.NewGuid(), Name = "one", Inner = new InnerDoc { Value = 10, Text = "alpha" } };
        var doc2 = new DocWithInner { Id = Guid.NewGuid(), Name = "two", Inner = new InnerDoc { Value = 50, Text = "beta" } };
        var doc3 = new DocWithInner { Id = Guid.NewGuid(), Name = "three", Inner = new InnerDoc { Value = 90, Text = "gamma" } };

        theSession.Store(doc1, doc2, doc3);
        await theSession.SaveChangesAsync();

        // Multiple Where clauses after Select
        var results = await theSession.Query<DocWithInner>()
            .Select(x => x.Inner)
            .Where(x => x.Value > 5)
            .Where(x => x.Value < 80)
            .ToListAsync();

        results.Count.ShouldBe(2);
        results.ShouldContain(x => x.Value == 10);
        results.ShouldContain(x => x.Value == 50);
    }

    [Fact]
    public async Task select_before_where_with_string_comparison()
    {
        var doc1 = new DocWithInner { Id = Guid.NewGuid(), Name = "one", Inner = new InnerDoc { Value = 10, Text = "alpha" } };
        var doc2 = new DocWithInner { Id = Guid.NewGuid(), Name = "two", Inner = new InnerDoc { Value = 50, Text = "beta" } };
        var doc3 = new DocWithInner { Id = Guid.NewGuid(), Name = "three", Inner = new InnerDoc { Value = 90, Text = "gamma" } };

        theSession.Store(doc1, doc2, doc3);
        await theSession.SaveChangesAsync();

        var results = await theSession.Query<DocWithInner>()
            .Select(x => x.Inner)
            .Where(x => x.Text == "beta")
            .ToListAsync();

        results.Count.ShouldBe(1);
        results[0].Value.ShouldBe(50);
    }

    [Fact]
    public async Task select_before_where_with_first_or_default()
    {
        var doc1 = new DocWithInner { Id = Guid.NewGuid(), Name = "one", Inner = new InnerDoc { Value = 10, Text = "alpha" } };
        var doc2 = new DocWithInner { Id = Guid.NewGuid(), Name = "two", Inner = new InnerDoc { Value = 50, Text = "beta" } };

        theSession.Store(doc1, doc2);
        await theSession.SaveChangesAsync();

        var result = await theSession.Query<DocWithInner>()
            .Select(x => x.Inner)
            .Where(x => x.Text == "beta")
            .FirstOrDefaultAsync();

        result.ShouldNotBeNull();
        result.Value.ShouldBe(50);
    }

    [Fact]
    public async Task select_before_where_with_count()
    {
        var doc1 = new DocWithInner { Id = Guid.NewGuid(), Name = "one", Inner = new InnerDoc { Value = 10, Text = "alpha" } };
        var doc2 = new DocWithInner { Id = Guid.NewGuid(), Name = "two", Inner = new InnerDoc { Value = 50, Text = "beta" } };
        var doc3 = new DocWithInner { Id = Guid.NewGuid(), Name = "three", Inner = new InnerDoc { Value = 90, Text = "gamma" } };

        theSession.Store(doc1, doc2, doc3);
        await theSession.SaveChangesAsync();

        var count = await theSession.Query<DocWithInner>()
            .Select(x => x.Inner)
            .Where(x => x.Value > 40)
            .CountAsync();

        count.ShouldBe(2);
    }

    [Fact]
    public async Task where_before_and_after_select()
    {
        var doc1 = new DocWithInner { Id = Guid.NewGuid(), Name = "one", Inner = new InnerDoc { Value = 10, Text = "alpha" } };
        var doc2 = new DocWithInner { Id = Guid.NewGuid(), Name = "two", Inner = new InnerDoc { Value = 50, Text = "beta" } };
        var doc3 = new DocWithInner { Id = Guid.NewGuid(), Name = "three", Inner = new InnerDoc { Value = 90, Text = "gamma" } };

        theSession.Store(doc1, doc2, doc3);
        await theSession.SaveChangesAsync();

        // Pre-Select Where on document + post-Select Where on projected type
        var results = await theSession.Query<DocWithInner>()
            .Where(x => x.Name != "one")
            .Select(x => x.Inner)
            .Where(x => x.Value < 80)
            .ToListAsync();

        results.Count.ShouldBe(1);
        results[0].Value.ShouldBe(50);
    }

    [Fact]
    public async Task select_deep_member_before_where()
    {
        var doc1 = new DocWithNested
        {
            Id = Guid.NewGuid(),
            Level1 = new Level1Doc
            {
                Level2 = new Level2Doc { Score = 100, Label = "a" }
            }
        };
        var doc2 = new DocWithNested
        {
            Id = Guid.NewGuid(),
            Level1 = new Level1Doc
            {
                Level2 = new Level2Doc { Score = 200, Label = "b" }
            }
        };

        theSession.Store(doc1, doc2);
        await theSession.SaveChangesAsync();

        // Select a deeply nested member, then filter
        var results = await theSession.Query<DocWithNested>()
            .Select(x => x.Level1.Level2)
            .Where(x => x.Score > 150)
            .ToListAsync();

        results.Count.ShouldBe(1);
        results[0].Label.ShouldBe("b");
    }
}

public class DocWithInner
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public InnerDoc Inner { get; set; }
}

public class InnerDoc
{
    public int Value { get; set; }
    public string Text { get; set; }
}

public class DocWithNested
{
    public Guid Id { get; set; }
    public Level1Doc Level1 { get; set; }
}

public class Level1Doc
{
    public Level2Doc Level2 { get; set; }
}

public class Level2Doc
{
    public int Score { get; set; }
    public string Label { get; set; }
}
