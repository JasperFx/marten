using System.Linq;
using Marten.Schema.Identity;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Writing.Identity;

public class StringIdGenerationTester
{
    [Fact]
    public void supports_strings()
    {
        new StringIdGeneration().KeyTypes.Single()
            .ShouldBe(typeof(string));
    }

}