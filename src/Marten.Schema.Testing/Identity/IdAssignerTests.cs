using System;
using Baseline.Reflection;
using Marten.Schema.Identity;
using Marten.Schema.Testing.Documents;
using Shouldly;
using Xunit;

namespace Marten.Schema.Testing.Identity
{
    public class IdAssignerTests
    {
        private IdAssigner<User, Guid> theAssigner;

        public IdAssignerTests()
        {
            var member = ReflectionHelper.GetProperty<User>(x => x.Id);
            theAssigner = new IdAssigner<User, Guid>(member, new GuidIdGeneration());
        }

        [Fact]
        public void assign_a_given_id()
        {
            var user = new User();
            var id = Guid.NewGuid();

            theAssigner.Assign(null, user, id);

            user.Id.ShouldBe(id);
        }


    }

    public class IdAssignerTestsPrivateFields
    {
        [Fact]
        public void assign_a_given_id_setter_is_private()
        {
            var member = ReflectionHelper.GetProperty<UserWithPrivateId>(x => x.Id);
            var theAssigner = new IdAssigner<UserWithPrivateId, Guid>(member, new GuidIdGeneration());
            var user = new UserWithPrivateId();
            var id = Guid.NewGuid();

            theAssigner.Assign(null, user, id);

            user.Id.ShouldBe(id);
        }
    }
}
