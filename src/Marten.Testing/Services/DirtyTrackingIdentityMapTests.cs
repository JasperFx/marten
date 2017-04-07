using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Baseline;
using Marten.Services;
using Shouldly;
using Xunit;

namespace Marten.Testing.Services
{

    public static class StringToTextReaderExtensions
    {
        public static TextReader ToReader(this string json)
        {
            return new StringReader(json);
        }
    }

    public class DirtyTrackingIdentityMapTests
    {
        [Fact]
        public void get_value_on_first_request()
        {
            var target = Target.Random();

            var serializer = new TestsSerializer();

            var map = new DirtyTrackingIdentityMap(serializer, null);

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

            var map = new DirtyTrackingIdentityMap(serializer, null);

            map.Get<NulloIdentityMapTests.Car>(camaro.Id, typeof(NulloIdentityMapTests.Camaro), json.ToReader(), null)
                .ShouldBeOfType<NulloIdentityMapTests.Camaro>()
                .Id.ShouldBe(camaro.Id);


        }

        [Fact]
        public void get_value_on_subsequent_requests()
        {
            var target = Target.Random();

            var serializer = new TestsSerializer();

            var map = new DirtyTrackingIdentityMap(serializer, null);

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
        public void get_value_on_first_request_with_lazy_json()
        {
            var target = Target.Random();

            var serializer = new TestsSerializer();

            var map = new DirtyTrackingIdentityMap(serializer, null);

            var json = serializer.ToJson(target);
            var clonedTarget = serializer.FromJson<Target>(json);

            var target2 = map.Get<Target>(target.Id, () =>
            {
                
                return new FetchResult<Target>(clonedTarget, json, null);
            });

            target2.Id.ShouldBe(target.Id);
            target2.ShouldNotBeTheSameAs(target);
        }

        [Fact]
        public void get_value_on_subsequent_requests_with_lazy_json()
        {
            var target = Target.Random();

            var serializer = new TestsSerializer();

            var map = new DirtyTrackingIdentityMap(serializer, null);

            var target2 = map.Get<Target>(target.Id, () => new FetchResult<Target>(target, serializer.ToJson(target), null));
            var target3 = map.Get<Target>(target.Id, () => new FetchResult<Target>(target, serializer.ToJson(target), null));
            var target4 = map.Get<Target>(target.Id, () => new FetchResult<Target>(target, serializer.ToJson(target), null));
            var target5 = map.Get<Target>(target.Id, () => new FetchResult<Target>(target, serializer.ToJson(target), null));

            target2.Id.ShouldBe(target.Id);
            target3.Id.ShouldBe(target.Id);
            target4.Id.ShouldBe(target.Id);
            target5.Id.ShouldBe(target.Id);

            target2.ShouldBeTheSameAs(target3);
            target2.ShouldBeTheSameAs(target4);
            target2.ShouldBeTheSameAs(target5);
        }

        [Fact]
        public void detect_changes_with_no_changes()
        {
            var a = Target.Random();
            var b = Target.Random();
            var c = Target.Random();
            var d = Target.Random();

            var serializer = new TestsSerializer();

            var map = new DirtyTrackingIdentityMap(serializer, null);


            var a1 = map.Get<Target>(a.Id, serializer.ToJson(a).ToReader(), null);
            var b1 = map.Get<Target>(a.Id, serializer.ToJson(b).ToReader(), null);
            var c1 = map.Get<Target>(a.Id, serializer.ToJson(c).ToReader(), null);
            var d1 = map.Get<Target>(a.Id, serializer.ToJson(d).ToReader(), null);

            // no changes

            map.DetectChanges().Any().ShouldBeFalse();
        }


        [Fact]
        public void detect_changes_with_multiple_dirties()
        {
            var a = Target.Random();
            var b = Target.Random();
            var c = Target.Random();
            var d = Target.Random();

            var serializer = new TestsSerializer();

            var map = new DirtyTrackingIdentityMap(serializer, null);


            var a1 = map.Get<Target>(a.Id, serializer.ToJson(a).ToReader(), null);
            a1.Long++;

            var b1 = map.Get<Target>(b.Id, serializer.ToJson(b).ToReader(), null);
            var c1 = map.Get<Target>(c.Id, serializer.ToJson(c).ToReader(), null);
            c1.Long++;

            var d1 = map.Get<Target>(d.Id, serializer.ToJson(d).ToReader(), null);


            var changes = map.DetectChanges();
            changes.Count().ShouldBe(2);
            changes.Any(x => x.Id.As<Guid>() == a1.Id).ShouldBeTrue();
            changes.Any(x => x.Id.As<Guid>() == c1.Id).ShouldBeTrue();
        }

        [Fact]
        public void detect_changes_then_clear_the_changes()
        {
            var a = Target.Random();
            var b = Target.Random();
            var c = Target.Random();
            var d = Target.Random();

            var serializer = new TestsSerializer();

            var map = new DirtyTrackingIdentityMap(serializer, null);


            var a1 = map.Get<Target>(a.Id, serializer.ToJson(a).ToReader(), null);
            a1.Long++;

            var b1 = map.Get<Target>(b.Id, serializer.ToJson(b).ToReader(), null);
            var c1 = map.Get<Target>(c.Id, serializer.ToJson(c).ToReader(), null);
            c1.Long++;

            var d1 = map.Get<Target>(d.Id, serializer.ToJson(d).ToReader(), null);


            var changes = map.DetectChanges();

            changes.Each(x => x.ChangeCommitted());


            map.DetectChanges().Any().ShouldBeFalse();
        }

        [Fact]
        public void remove_item()
        {
            var target = Target.Random();
            var target2 = Target.Random();
            target2.Id = target.Id;

            var serializer = new TestsSerializer();

            var map = new DirtyTrackingIdentityMap(serializer, null);

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

            var map = new DirtyTrackingIdentityMap(serializer, null);

            map.Store(target.Id, target);


            map.Get<Target>(target.Id, "".ToReader(), null).ShouldBeTheSameAs(target);
        }

        [Fact]
        public void get_with_miss_in_database()
        {
            var serializer = new TestsSerializer();

            var map = new DirtyTrackingIdentityMap(serializer, null);
            map.Get<Target>(Guid.NewGuid(), () => null).ShouldBeNull();
        }

        [Fact]
        public void has_positive()
        {
            var target = Target.Random();
            var serializer = new TestsSerializer();

            var map = new DirtyTrackingIdentityMap(serializer, null);

            map.Store(target.Id, target);

            map.Has<Target>(target.Id).ShouldBeTrue();

        }

        [Fact]
        public void has_negative()
        {
            var serializer = new TestsSerializer();

            var map = new DirtyTrackingIdentityMap(serializer, null);
            map.Has<Target>(Guid.NewGuid()).ShouldBeFalse();
        }

        [Fact]
        public void retrieve()
        {
            var target = Target.Random();
            var serializer = new TestsSerializer();

            var map = new DirtyTrackingIdentityMap(serializer, null);

            map.Store(target.Id, target);

            map.Retrieve<Target>(target.Id).ShouldBeTheSameAs(target);
        }
    }
}