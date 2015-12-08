using System;
using Marten.Services;
using Marten.Testing.Fixtures;
using Shouldly;

namespace Marten.Testing.Services
{
    public class NulloIdentityMapTests
    {
        public void lazy_get_hit()
        {
            var serializer = new JilSerializer();
            var target = new Target();
            var json = serializer.ToJson(target);

            var map = new NulloIdentityMap(serializer);

            var target2 = map.Get<Target>(target.Id, () => json);
            target2.Id.ShouldBe(target.Id);
           
        }

        public void lazy_get_miss()
        {
            var map = new NulloIdentityMap(new JilSerializer());

            map.Get<Target>(Guid.NewGuid(), () => null).ShouldBeNull();
        }

        public void get_with_json()
        {
            var serializer = new JilSerializer();
            var target = new Target();
            var json = serializer.ToJson(target);

            var map = new NulloIdentityMap(serializer);

            var target2 = map.Get<Target>(target.Id, json);
            target2.Id.ShouldBe(target.Id);
        }

        
    }
}