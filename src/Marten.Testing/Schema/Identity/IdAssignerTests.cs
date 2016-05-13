using System;
using Baseline.Reflection;
using Marten.Schema;
using Marten.Schema.Identity;
using Marten.Testing.Documents;
using Shouldly;
using Xunit;

namespace Marten.Testing.Schema.Identity
{
    public class IdAssignerTests
    {
        private IdAssigner<User, Guid> theAssigner;

        public IdAssignerTests()
        {
            var member = ReflectionHelper.GetProperty<User>(x => x.Id);
            theAssigner = new IdAssigner<User, Guid>(member, new GuidIdGeneration(), null);
        }

        [Fact]
        public void assign_a_given_id()
        {
            var user = new User();
            var id = Guid.NewGuid();

            theAssigner.Assign(user, id);

            user.Id.ShouldBe(id);
        }

        [Fact]
        public void no_need_to_assign()
        {
            bool assigned = true;

            var originalId = Guid.NewGuid();
            var user = new User {Id = originalId};

            theAssigner.Assign(user, out assigned);

            user.Id.ShouldBe(originalId);
            assigned.ShouldBeFalse();
        }


        [Fact]
        public void needs_to_assign_new_value()
        {
            bool assigned = false;

            var user = new User {Id = Guid.Empty};

            theAssigner.Assign(user, out assigned);

            assigned.ShouldBeTrue();
            user.Id.ShouldNotBe(Guid.Empty);
        }
    }
}