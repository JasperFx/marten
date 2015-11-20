using Marten.Services;
using Marten.Testing.Fixtures;
using Octokit;
using Shouldly;

namespace Marten.Testing.Services
{
    public class IdentityMapTests
    {
        public void get_value_on_first_request()
        {
            var target = Target.Random();

            var serializer = new JilSerializer();

            var map = new IdentityMap(serializer);

            var target2 = map.Get<Target>(target.Id, serializer.ToJson(target));

            target2.Id.ShouldBe(target.Id);
            target2.ShouldNotBeTheSameAs(target);
        }

        public void get_value_on_subsequent_requests()
        {
            var target = Target.Random();

            var serializer = new JilSerializer();

            var map = new IdentityMap(serializer);

            var target2 = map.Get<Target>(target.Id, serializer.ToJson(target));
            var target3 = map.Get<Target>(target.Id, serializer.ToJson(target));
            var target4 = map.Get<Target>(target.Id, serializer.ToJson(target));
            var target5 = map.Get<Target>(target.Id, serializer.ToJson(target));

            target2.Id.ShouldBe(target.Id);
            target3.Id.ShouldBe(target.Id);
            target4.Id.ShouldBe(target.Id);
            target5.Id.ShouldBe(target.Id);

            target2.ShouldBeTheSameAs(target3);
            target2.ShouldBeTheSameAs(target4);
            target2.ShouldBeTheSameAs(target5);
        }

        public void get_value_on_first_request_with_lazy_json()
        {
            var target = Target.Random();

            var serializer = new JilSerializer();

            var map = new IdentityMap(serializer);

            var target2 = map.Get<Target>(target.Id, () => serializer.ToJson(target));

            target2.Id.ShouldBe(target.Id);
            target2.ShouldNotBeTheSameAs(target);
        }

        public void get_value_on_subsequent_requests_with_lazy_json()
        {
            var target = Target.Random();

            var serializer = new JilSerializer();

            var map = new IdentityMap(serializer);

            var target2 = map.Get<Target>(target.Id, () => serializer.ToJson(target));
            var target3 = map.Get<Target>(target.Id, () => serializer.ToJson(target));
            var target4 = map.Get<Target>(target.Id, () => serializer.ToJson(target));
            var target5 = map.Get<Target>(target.Id, () => serializer.ToJson(target));

            target2.Id.ShouldBe(target.Id);
            target3.Id.ShouldBe(target.Id);
            target4.Id.ShouldBe(target.Id);
            target5.Id.ShouldBe(target.Id);

            target2.ShouldBeTheSameAs(target3);
            target2.ShouldBeTheSameAs(target4);
            target2.ShouldBeTheSameAs(target5);
        }
    }
}