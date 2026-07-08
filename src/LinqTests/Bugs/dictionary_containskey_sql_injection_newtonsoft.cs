using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Newtonsoft;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace LinqTests.Bugs;

// Security regression for Dictionary.ContainsKey under the Newtonsoft serializer, which
// (unlike System.Text.Json) leaves a single quote unescaped in ToCleanJson output. The
// key is appended into a '{ ... }' JSON-path literal, so an unescaped quote could break
// out and inject an always-true predicate. The fix escapes embedded single quotes.
public class dictionary_containskey_sql_injection_newtonsoft : OneOffConfigurationsContext
{
    public class AttributeDoc
    {
        public Guid Id { get; set; }
        public Dictionary<string, string> Attributes { get; set; } = new();
    }

    [Fact]
    public async Task containskey_quote_cannot_break_out_under_newtonsoft()
    {
        var store = SeparateStore(opts =>
        {
            opts.UseNewtonsoftForSerialization();
            opts.DatabaseSchemaName = "dict_sqli";
            opts.Schema.For<AttributeDoc>().DocumentAlias("attr_doc");
        });

        await store.Advanced.Clean.CompletelyRemoveAllAsync();

        await using (var session = store.LightweightSession())
        {
            session.Store(new AttributeDoc { Attributes = new() { { "color", "red" } } });
            session.Store(new AttributeDoc { Attributes = new() { { "color", "green" } } });
            session.Store(new AttributeDoc { Attributes = new() { { "color", "blue" } } });
            await session.SaveChangesAsync();
        }

        await using (var session = store.QuerySession())
        {
            var attackKey = "nonexistent'}' is not null or '1'='1";
            var attack = await session.Query<AttributeDoc>()
                .Where(x => x.Attributes.ContainsKey(attackKey)).CountAsync();

            attack.ShouldBe(0);
        }
    }
}
