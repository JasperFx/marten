using System;
using Marten.Services;
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

            var serializer = new TestsSerializer();

            var map = new IdentityMap(serializer, null);

            var target2 = map.Get<Target>(target.Id, serializer.ToJson(target).ToReader(), null);

            target2.Id.ShouldBe(target.Id);
            target2.ShouldNotBeTheSameAs(target);
        }

        [Fact]
        public void get_with_concrete_type()
        {
            var serializer = new JsonNetSerializer();
            var camaro = new NulloIdentityMapTests.Camaro();

            var json = serializer.ToJson(camaro);

            var map = new IdentityMap(serializer, null);

            map.Get<NulloIdentityMapTests.Car>(camaro.Id, typeof(NulloIdentityMapTests.Camaro), json.ToReader(), null)
                .ShouldBeOfType<NulloIdentityMapTests.Camaro>()
                .Id.ShouldBe(camaro.Id);


        }

        [Fact]
        public void get_value_on_subsequent_requests()
        {
            var target = Target.Random();

            var serializer = new TestsSerializer();

            var map = new IdentityMap(serializer, null);

            var target2 = map.Get<Target>(target.Id, serializer.ToJson(target).ToReader(), null);
            var target3 = map.Get<Target>(target.Id, serializer.ToJson(target).ToReader(), null);
            var target4 = map.Get<Target>(target.Id, serializer.ToJson(target).ToReader(), null);
            var target5 = map.Get<Target>(target.Id, serializer.ToJson(target).ToReader(), null);

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

            var serializer = new TestsSerializer();

            var map = new IdentityMap(serializer, null);

            var target3 = map.Get<Target>(target.Id, serializer.ToJson(target).ToReader(), null);

            // now remove it
            map.Remove<Target>(target.Id);

            var target4 = map.Get<Target>(target.Id, serializer.ToJson(target2).ToReader(), null);
            target4.ShouldNotBeNull();
            target4.ShouldNotBeTheSameAs(target3);

        }

        [Fact]
        public void store()
        {
            var target = Target.Random();
            var serializer = new TestsSerializer();

            var map = new IdentityMap(serializer, null);

            map.Store(target.Id, target);


            map.Get<Target>(target.Id, "".ToReader(), null).ShouldBeTheSameAs(target);
        }

        [Fact]
        public void has_positive()
        {
            var target = Target.Random();
            var serializer = new TestsSerializer();

            var map = new IdentityMap(serializer, null);

            map.Store(target.Id, target);

            map.Has<Target>(target.Id).ShouldBeTrue();

        }

        [Fact]
        public void has_negative()
        {
            var serializer = new TestsSerializer();

            var map = new IdentityMap(serializer, null);
            map.Has<Target>(Guid.NewGuid()).ShouldBeFalse();
        }

        [Fact]
        public void retrieve()
        {
            var target = Target.Random();
            var serializer = new TestsSerializer();

            var map = new IdentityMap(serializer, null);

            map.Store(target.Id, target);

            map.Retrieve<Target>(target.Id).ShouldBeTheSameAs(target);

        }
    }
}