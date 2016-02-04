using System;
using Marten.Services;
using Marten.Testing.Fixtures;
using Octokit;
using Shouldly;
using Xunit;

namespace Marten.Testing.Services
{
    public class IdentityMapTests
    {
        [Fact]
        public void get_value_on_first_request()
        {
            var target = Target.Random();

            var serializer = new JilSerializer();

            var map = new IdentityMap(serializer);

            var target2 = map.Get<Target>(target.Id, serializer.ToJson(target));

            target2.Id.ShouldBe(target.Id);
            target2.ShouldNotBeTheSameAs(target);
        }

        [Fact]
        public void get_with_concrete_type()
        {
            var serializer = new JsonNetSerializer();
            var camaro = new NulloIdentityMapTests.Camaro();

            var json = serializer.ToJson(camaro);

            var map = new IdentityMap(serializer);

            map.Get<NulloIdentityMapTests.Car>(camaro.Id, typeof(NulloIdentityMapTests.Camaro), json)
                .ShouldBeOfType<NulloIdentityMapTests.Camaro>()
                .Id.ShouldBe(camaro.Id);


        }

        [Fact]
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

        [Fact]
        public void remove_item()
        {
            var target = Target.Random();
            var target2 = Target.Random();
            target2.Id = target.Id;

            var serializer = new JilSerializer();

            var map = new IdentityMap(serializer);

            var target3 = map.Get<Target>(target.Id, serializer.ToJson(target));

            // now remove it
            map.Remove<Target>(target.Id);

            var target4 = map.Get<Target>(target.Id, serializer.ToJson(target2));
            target4.ShouldNotBeNull();
            target4.ShouldNotBeTheSameAs(target3);

        }

        [Fact]
        public void get_value_on_first_request_with_lazy_json()
        {
            var target = Target.Random();

            var serializer = new JilSerializer();

            var map = new IdentityMap(serializer);

            var target2 = map.Get<Target>(target.Id, () => serializer.ToJson(target));

            target2.Id.ShouldBe(target.Id);
            target2.ShouldNotBeTheSameAs(target);
        }

        [Fact]
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

        [Fact]
        public void store()
        {
            var target = Target.Random();
            var serializer = new JilSerializer();

            var map = new IdentityMap(serializer);

            map.Store(target.Id, target);


            map.Get<Target>(target.Id, "").ShouldBeTheSameAs(target);
        }

        [Fact]
        public void get_with_miss_in_database()
        {
            var serializer = new JilSerializer();

            var map = new IdentityMap(serializer);
            map.Get<Target>(Guid.NewGuid(), () => null).ShouldBeNull();
        }

        [Fact]
        public void has_positive()
        {
            var target = Target.Random();
            var serializer = new JilSerializer();

            var map = new IdentityMap(serializer);

            map.Store(target.Id, target);

            map.Has<Target>(target.Id).ShouldBeTrue();

        }

        [Fact]
        public void has_negative()
        {
            var serializer = new JilSerializer();

            var map = new IdentityMap(serializer);
            map.Has<Target>(Guid.NewGuid()).ShouldBeFalse();
        }

        [Fact]
        public void retrieve()
        {
            var target = Target.Random();
            var serializer = new JilSerializer();

            var map = new IdentityMap(serializer);

            map.Store(target.Id, target);

            map.Retrieve<Target>(target.Id).ShouldBeTheSameAs(target);

        }
    }
}