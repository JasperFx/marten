using System;
using Marten.Schema.Identity;
using Shouldly;
using Xunit;

namespace Marten.Schema.Testing.Identity
{
    public class NoOpIdGenerationTests
    {
        [Fact]
        public void can_be_used_on_any_valid_id_type()
        {
            var generator = new NoOpIdGeneration();

            generator.KeyTypes.ShouldHaveTheSameElementsAs(typeof(int), typeof(long), typeof(string), typeof(Guid));
        }

    }
}
