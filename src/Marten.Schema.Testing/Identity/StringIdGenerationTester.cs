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

        [Fact]
        public void build_returns_itself()
        {
            var generator = new StringIdGeneration();

            generator.Build<string>().ShouldBeSameAs(generator);
        }

        [Fact]
        public void assign_with_a_value()
        {
            bool assigned = true;

            var generator = new StringIdGeneration();
            generator.Assign(null, "something", out assigned).ShouldBe("something");

            assigned.ShouldBeFalse();
        }

        [Fact]
        public void assign_when_empty()
        {
            bool assigned = true;

            var generator = new StringIdGeneration();


            Exception<InvalidOperationException>.ShouldBeThrownBy(() =>
            {
                generator.Assign(null, String.Empty, out assigned);
            });
        }

        [Fact]
        public void assign_when_null()
        {
            bool assigned = true;

            var generator = new StringIdGeneration();


            Exception<InvalidOperationException>.ShouldBeThrownBy(() =>
            {
                generator.Assign(null, null, out assigned);
            });
        }
    }
}
