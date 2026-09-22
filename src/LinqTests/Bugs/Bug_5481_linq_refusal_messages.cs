using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Exceptions;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace LinqTests.Bugs;

/// <summary>
///     #5481. Three LINQ refusal messages that said less than they could: a literal '$' leaked into
///     the SimpleExpression text, the dictionary refusal was a joke rather than a diagnosis, and the
///     two generic operator refusals named no escape hatch at all.
/// </summary>
public class Bug_5481_linq_refusal_messages: BugIntegrationContext
{
    public class DictionaryTarget
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public Dictionary<string, string> Attributes { get; set; } = new();
    }

    [Fact]
    public async Task unknown_linq_operator_names_the_escape_hatches()
    {
        var ex = await Should.ThrowAsync<BadLinqExpressionException>(async () =>
            await theSession.Query<DictionaryTarget>().Reverse().ToListAsync());

        ex.Message.ShouldContain("Marten does not (yet) support Linq operator 'Reverse'.");
        assertNamesTheEscapeHatches(ex.Message);
    }

    [Fact]
    public async Task unsupported_method_call_names_the_escape_hatches()
    {
        var ex = await Should.ThrowAsync<NotSupportedException>(async () =>
            await theSession.Query<DictionaryTarget>()
                .Where(x => x.Name.IsNormalized()).ToListAsync());

        ex.Message.ShouldContain("Marten does not (yet) support Linq queries using the");
        assertNamesTheEscapeHatches(ex.Message);
    }

    private static void assertNamesTheEscapeHatches(string message)
    {
        message.ShouldContain("martendb.io/documents/querying/linq/operators.html");
        message.ShouldContain("MatchesSql()");
        message.ShouldContain("QueryAsync<T>(sql)");
        message.ShouldContain("ToListAsync()");
    }
}
