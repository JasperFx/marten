using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Bugs;

public class is_null_or_white_space_matches_all_whitespace: BugIntegrationContext
{
    public record WhiteSpaceDocument(Guid Id, string Name);

    [Theory]
    [InlineData("\t")]
    [InlineData("\r\n")]
    [InlineData(" \n ")]
    [InlineData("\f\v")]
    public async Task whitespace_only_value_is_null_or_white_space(string value)
    {
        var whiteSpace = new WhiteSpaceDocument(Guid.NewGuid(), value);
        var text = new WhiteSpaceDocument(Guid.NewGuid(), "\ttext\n");
        theSession.Store(whiteSpace, text);
        await theSession.SaveChangesAsync();

        var matching = await theSession.Query<WhiteSpaceDocument>()
            .Where(x => string.IsNullOrWhiteSpace(x.Name))
            .ToListAsync();

        matching.Select(x => x.Id).ShouldBe([whiteSpace.Id]);

        var notMatching = await theSession.Query<WhiteSpaceDocument>()
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .ToListAsync();

        notMatching.Select(x => x.Id).ShouldBe([text.Id]);
    }
}
