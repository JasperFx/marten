using Baseline.Reflection;
using Marten.Testing.Fixtures;
using Marten.Util;
using Shouldly;
using Xunit;

namespace Marten.Testing.Util
{
    public class LambdaBuilderTester
    {
        [Fact]
        public void can_build_getter_for_property()
        {
            var target = new Target {Number = 5};
            var prop = ReflectionHelper.GetProperty<Target>(x => x.Number);

            var getter = LambdaBuilder.GetProperty<Target, int>(prop);

            getter(target).ShouldBe(target.Number);
        }
    }
}