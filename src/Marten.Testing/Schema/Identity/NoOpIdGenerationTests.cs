using System;
using Marten.Schema.Identity;
using Shouldly;
using Xunit;

namespace Marten.Testing.Schema.Identity
{
    public class NoOpIdGenerationTests
    {
        [Fact]
        public void can_be_used_on_any_valid_id_type()
        {
            var generator = new NoOpIdGeneration();

            generator.KeyTypes.ShouldHaveTheSameElementsAs(typeof(int), typeof(long), typeof(string), typeof(Guid));
        }

        [Fact]
        public void never_assign_anything()
        {
            var generator = new NoOpIdGeneration();
            var ids = generator.Build<int>();

            bool assigned = true;
            ids.Assign(null, 5, out assigned).ShouldBe(5);

            assigned.ShouldBeFalse();
        }
    }
}