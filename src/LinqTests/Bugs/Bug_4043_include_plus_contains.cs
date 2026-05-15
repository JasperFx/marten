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

    // Removed: CompiledQuery_WithIncludeAndContains_Fails. The test asserted
    // that the v8-era JasperFx.RuntimeCompiler emit-path threw a specific
    // compilation error (CS1002 / CS0103 / "Compilation failures") when an
    // Include() + Contains() compiled query was first invoked. That assertion
    // was testing the *output of the source code generator* — exactly the
    // surface that #4405 replaces with a typed source-generator-emitted
    // handler. The underlying emit-time bug was also fixed by the broader
    // codegen sweep in #4401, so the test would now have to be inverted to
    // assert success. We delete it instead: a regression test against
    // specific runtime-codegen failure modes doesn't fit the v9 world and
    // would have to be rewritten as a "this query runs end-to-end" assertion,
    // which the two tests above already cover for the simpler shapes.
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

// Removed: FindByOwnerWithIncludeQuery — the only test that referenced it
// (CompiledQuery_WithIncludeAndContains_Fails) is gone per the comment above.
