using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Marten.Linq;
using Marten.Testing.Harness;

namespace LinqTests.Bugs;

public class Bug_4043_include_plus_contains : BugIntegrationContext
{
    /// <summary>
    /// Contains() alone in compiled query - WORKS
    /// </summary>
    [Fact]
    public async Task CompiledQuery_WithContainsOnly_Works()
    {
        var ownerId = Guid.NewGuid();
        var document = new TestDocument
        {
            Id = Guid.NewGuid(),
            Name = "Test",
            Owners = [ownerId]
        };

        await using (var session = theStore!.LightweightSession())
        {
            session.Store(document);
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.QuerySession())
        {
            var query = new FindByOwnerQuery { OwnerId = ownerId };
            var results = await session.QueryAsync(query);

            Assert.Single(results);
            Assert.Equal(document.Id, results.First().Id);
        }
    }

    /// <summary>
    /// Include() alone in compiled query - WORKS
    /// </summary>
    [Fact]
    public async Task CompiledQuery_WithIncludeOnly_Works()
    {
        var relatedDoc = new RelatedDocument { Id = Guid.NewGuid(), Description = "Related" };
        var document = new TestDocument
        {
            Id = Guid.NewGuid(),
            Name = "Test",
            Owners = [],
            RelatedDocumentId = relatedDoc.Id
        };

        await using (var session = theStore!.LightweightSession())
        {
            session.Store(relatedDoc);
            session.Store(document);
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.QuerySession())
        {
            var query = new FindByIdWithIncludeQuery { DocumentId = document.Id };
            var results = await session.QueryAsync(query);

            Assert.Single(results);
            Assert.Single(query.RelatedDocuments);
        }
    }

    /// <summary>
    /// Include() + Contains() in compiled query - FAILS with code generation error!
    ///
    /// The generated C# code contains unescaped JSON:
    ///   parameters[0].Value = "{"Owners":["guid"]}";  // INVALID
    /// Should be:
    ///   parameters[0].Value = "{\"Owners\":[\"guid\"]}";  // VALID
    /// </summary>
    [Fact]
    public async Task CompiledQuery_WithIncludeAndContains_Fails()
    {
        var ownerId = Guid.NewGuid();
        var relatedDoc = new RelatedDocument { Id = Guid.NewGuid(), Description = "Related" };
        var document = new TestDocument
        {
            Id = Guid.NewGuid(),
            Name = "Test",
            Owners = [ownerId],
            RelatedDocumentId = relatedDoc.Id
        };

        await using (var session = theStore!.LightweightSession())
        {
            session.Store(relatedDoc);
            session.Store(document);
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.QuerySession())
        {
            var query = new FindByOwnerWithIncludeQuery { OwnerId = ownerId };

            // This throws due to invalid code generation
            var exception = await Assert.ThrowsAnyAsync<Exception>(async () =>
            {
                await session.QueryAsync(query);
            });

            // Should contain CS1002 or similar code generation error
            Assert.True(
                exception.Message.Contains("CS1002") ||
                exception.Message.Contains("CS0103") ||
                exception.Message.Contains("Compilation failures"),
                $"Expected code generation error, got: {exception.Message}");
        }
    }
}


public class TestDocument
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public HashSet<Guid> Owners { get; set; } = [];
    public Guid RelatedDocumentId { get; set; }
}

public class RelatedDocument
{
    public Guid Id { get; set; }
    public string Description { get; set; } = string.Empty;
}


public class FindByOwnerQuery : ICompiledListQuery<TestDocument>
{
    public Guid OwnerId { get; set; }

    public Expression<Func<IMartenQueryable<TestDocument>, IEnumerable<TestDocument>>> QueryIs()
    {
        return q => q.Where(x => x.Owners.Contains(OwnerId));
    }
}

public class FindByIdWithIncludeQuery : ICompiledListQuery<TestDocument>
{
    public Guid DocumentId { get; set; }
    public Dictionary<Guid, RelatedDocument> RelatedDocuments { get; } = [];

    public Expression<Func<IMartenQueryable<TestDocument>, IEnumerable<TestDocument>>> QueryIs()
    {
        return q => q
            .Include(x => x.RelatedDocumentId, RelatedDocuments)
            .Where(x => x.Id == DocumentId);
    }
}

public class FindByOwnerWithIncludeQuery : ICompiledListQuery<TestDocument>
{
    public Guid OwnerId { get; set; }
    public Dictionary<Guid, RelatedDocument> RelatedDocuments { get; } = [];

    public Expression<Func<IMartenQueryable<TestDocument>, IEnumerable<TestDocument>>> QueryIs()
    {
        return q => q
            .Include(x => x.RelatedDocumentId, RelatedDocuments)
            .Where(x => x.Owners.Contains(OwnerId));
    }
}

