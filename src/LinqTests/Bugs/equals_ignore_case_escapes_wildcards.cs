using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten;
using Marten.Linq;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Bugs;

public class equals_ignore_case_escapes_wildcards: BugIntegrationContext
{
    public record EqualsIgnoreCaseDocument(Guid Id, string Name);

    public class FindByNameIgnoringCase: ICompiledListQuery<EqualsIgnoreCaseDocument>
    {
        public string Name { get; set; }

        public Expression<Func<IMartenQueryable<EqualsIgnoreCaseDocument>, IEnumerable<EqualsIgnoreCaseDocument>>> QueryIs()
        {
            return q => q.Where(x => x.Name.EqualsIgnoreCase(Name));
        }
    }

    [Theory]
    [InlineData("%MyString")]
    [InlineData("MyStrin_")]
    [InlineData("%")]
    public async Task wildcard_in_the_value_does_not_match_other_strings(string value)
    {
        theSession.Store(new EqualsIgnoreCaseDocument(Guid.NewGuid(), "MyString"));
        await theSession.SaveChangesAsync();

        var results = await theSession.Query<EqualsIgnoreCaseDocument>()
            .Where(x => x.Name.EqualsIgnoreCase(value))
            .ToListAsync();

        results.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("100% complete")]
    [InlineData("my_string")]
    [InlineData(@"c:\temp")]
    public async Task value_with_special_characters_matches_itself(string value)
    {
        var doc = new EqualsIgnoreCaseDocument(Guid.NewGuid(), value.ToUpperInvariant());
        theSession.Store(doc);
        await theSession.SaveChangesAsync();

        var results = await theSession.Query<EqualsIgnoreCaseDocument>()
            .Where(x => x.Name.EqualsIgnoreCase(value))
            .ToListAsync();

        results.Single().Id.ShouldBe(doc.Id);
    }

    [Fact]
    public async Task wildcard_in_a_compiled_query_does_not_match_other_strings()
    {
        var doc = new EqualsIgnoreCaseDocument(Guid.NewGuid(), "MyString");
        theSession.Store(doc);
        await theSession.SaveChangesAsync();

        (await theSession.QueryAsync(new FindByNameIgnoringCase { Name = "MyStrin_" })).ShouldBeEmpty();
        (await theSession.QueryAsync(new FindByNameIgnoringCase { Name = "mystring" })).Single().Id.ShouldBe(doc.Id);
    }
}
