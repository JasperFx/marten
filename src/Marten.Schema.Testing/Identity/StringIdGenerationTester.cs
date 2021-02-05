using System;
using System.Linq;
using Marten.Schema.Identity;
using Shouldly;
using Xunit;

namespace Marten.Schema.Testing.Identity
{
    public class StringIdGenerationTester
    {
        [Fact]
        public void supports_strings()
        {
            new StringIdGeneration().KeyTypes.Single()
                .ShouldBe(typeof(string));
        }

    }
}
