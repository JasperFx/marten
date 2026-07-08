using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace LinqTests.Bugs;

// Security regression: an attacker-influenced dictionary key must be treated as data,
// never concatenated into generated SQL. Historically the indexer key and the
// ContainsKey key were inlined into single-quoted JSON-path literals without escaping,
// which allowed a key containing a single quote to break out of the literal and inject
// an always-true predicate (filter / multi-tenant authorization bypass).
public class dictionary_key_sql_injection : BugIntegrationContext
{
    public class AttributeDoc
    {
        public System.Guid Id { get; set; }
        public Dictionary<string, string> Attributes { get; set; } = new();
    }

    private async Task seedThreeDocs()
    {
        theSession.Store(new AttributeDoc { Attributes = new() { { "color", "red" } } });
        theSession.Store(new AttributeDoc { Attributes = new() { { "color", "green" } } });
        theSession.Store(new AttributeDoc { Attributes = new() { { "color", "blue" } } });
        await theSession.SaveChangesAsync();
    }

    [Fact]
    public async Task indexer_key_with_quote_cannot_break_out_of_literal()
    {
        await seedThreeDocs();

        // Benign control: an honest missing key returns nothing.
        var benign = await theSession.Query<AttributeDoc>()
            .Where(x => x.Attributes["nonexistent-key"] == "v").CountAsync();
        benign.ShouldBe(0);

        // Attack: a key crafted to break out of the '...' literal and append `or 1=1 --`.
        // If the key is properly escaped/parameterized this stays an honest (empty) filter.
        var attackKey = "nonexistent' = '' or 1=1 --";
        var attack = await theSession.Query<AttributeDoc>()
            .Where(x => x.Attributes[attackKey] == "v").CountAsync();

        attack.ShouldBe(0);
    }

    [Fact]
    public async Task indexer_key_quote_is_escaped_in_generated_sql()
    {
        var attackKey = "a' or 1=1 --";
        var cmd = theSession.Query<AttributeDoc>()
            .Where(x => x.Attributes[attackKey] == "v").ToCommand();

        // The single quote must be doubled (escaped) so the literal is never broken.
        cmd.CommandText.ShouldContain("->> 'a'' or 1=1 --'");
    }

    [Fact]
    public async Task containskey_with_quote_cannot_break_out_of_literal()
    {
        await seedThreeDocs();

        var attackKey = "nonexistent'}' is not null or '1'='1";
        var attack = await theSession.Query<AttributeDoc>()
            .Where(x => x.Attributes.ContainsKey(attackKey)).CountAsync();

        attack.ShouldBe(0);
    }
}
