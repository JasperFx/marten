using System.Reflection;
using Baseline.Reflection;
using Marten.Testing.Documents;
using Marten.Util;
using Shouldly;
using Xunit;

namespace Marten.Testing.Util
{
    public class ReflectionExtensionsTests
    {
        [Fact]
        public void get_member_alias_with_one_prop()
        {
            var prop = ReflectionHelper.GetProperty<User>(x => x.FirstName);
            new MemberInfo[] {prop}.ToTableAlias().ShouldBe("first_name");
        }

        [Fact]
        public void get_member_alias_with_two_props()
        {
            var prop1 = ReflectionHelper.GetProperty<UserHolder>(x => x.User);
            var prop2 = ReflectionHelper.GetProperty<User>(x => x.FirstName);
            new MemberInfo[] { prop1, prop2 }.ToTableAlias().ShouldBe("user_first_name");
        }

        public class UserHolder
        {
            public User User { get; set; }
        }
    }
}