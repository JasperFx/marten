using System;
using Marten.Schema.Identity;
using Shouldly;
using Xunit;

namespace Marten.Testing.Schema.Identity
{
    public class GuidIdGeneratorTests
    {
        [Fact]
        public void do_nothing_with_an_existing_guid()
        {
            var existing = Guid.NewGuid();

            var generator = new GuidIdGenerator(Guid.NewGuid);

            bool assigned = true;

            generator.Assign(existing, out assigned).ShouldBe(existing);

            assigned.ShouldBeFalse();
        }

        [Fact]
        public void assign_a_new_guid_with_an_empty()
        {
            var newGuid = Guid.NewGuid();
            var generator = new GuidIdGenerator(() => newGuid);

            bool assigned = false;

            generator.Assign(Guid.Empty, out assigned)
                .ShouldBe(newGuid);

            assigned.ShouldBeTrue();
        }
    }
}