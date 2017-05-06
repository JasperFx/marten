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

            theAssigner.Assign(null, user, id);

            user.Id.ShouldBe(id);
        }

        [Fact]
        public void no_need_to_assign()
        {
            bool assigned = true;

            var originalId = Guid.NewGuid();
            var user = new User {Id = originalId};

            theAssigner.Assign(null, user, out assigned);

            user.Id.ShouldBe(originalId);
            assigned.ShouldBeFalse();
        }


        [Fact]
        public void needs_to_assign_new_value()
        {
            bool assigned = false;

            var user = new User {Id = Guid.Empty};

            theAssigner.Assign(null, user, out assigned);

            assigned.ShouldBeTrue();
            user.Id.ShouldNotBe(Guid.Empty);
        }
    }

    public class IdAssignerTestsPrivateFields
    { 
        [Fact]
        public void assign_a_given_id_setter_is_private()
        {
            var member = ReflectionHelper.GetProperty<UserWithPrivateId>(x => x.Id);
            var theAssigner = new IdAssigner<UserWithPrivateId, Guid>(member, new GuidIdGeneration(), null);
            var user = new UserWithPrivateId();
            var id = Guid.NewGuid();

            theAssigner.Assign(null, user, id);

            user.Id.ShouldBe(id);
        }
    }
}