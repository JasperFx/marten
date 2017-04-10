using System;
using Marten.Schema.Identity;
using Shouldly;
using Xunit;

namespace Marten.Testing.Schema.Identity
{
    public class StringUniqueIdGeneratorTests
    {
        [Fact]
        public void do_nothing_with_an_existing_value()
        {
			string existing = "someValue";

            var generator = new StringUniqueIdGeneration();

            bool assigned = true;

            generator.Assign(existing, out assigned).ShouldBe(existing);

            assigned.ShouldBeFalse();
        }

        [Fact]
        public void assign_a_new_value_with_an_empty()
        {
			string existing = string.Empty;
            var generator = new StringUniqueIdGeneration();
            bool assigned = false;
            generator.Assign(existing, out assigned)
                .ShouldNotBeNullOrEmpty();

            assigned.ShouldBeTrue();
        }

		[Fact]
		public void assign_a_new_value_with_an_null() {
			string existing = null;
			var generator = new StringUniqueIdGeneration();
			bool assigned = false;
			generator.Assign(existing, out assigned)
					.ShouldNotBeNullOrEmpty();

			assigned.ShouldBeTrue();
		}
	}
}