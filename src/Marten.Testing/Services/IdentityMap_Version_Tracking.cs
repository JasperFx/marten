using System;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Services;
using Shouldly;
using Xunit;

namespace Marten.Testing.Services
{
    public class NulloIdentityMap_version_tracking : IdentityMap_Version_Tracking<NulloIdentityMap> { }
    public class IdentityMap_version_tracking : IdentityMap_Version_Tracking<IdentityMap> { }
    public class DirtyTrackingIdentityMap_version_tracking : IdentityMap_Version_Tracking<DirtyTrackingIdentityMap> { }


    public abstract class IdentityMap_Version_Tracking<T> : DocumentSessionFixture<T> where T : IIdentityMap
    {
        private IIdentityMap theIdentityMap;

        public IdentityMap_Version_Tracking()
        {
            theIdentityMap = theSession.As<DocumentSession>().IdentityMap;
        }

        [Fact]
        public void store_by_version()
        {
            var target = Target.Random();
            var version = Guid.NewGuid();

            theIdentityMap.Store(target.Id, target, version);

            theIdentityMap.Versions.Version<Target>(target.Id)
                .ShouldBe(version);
        }

        [Fact]
        public void get_by_id_and_json()
        {
            var target = Target.Random();
            var json = theStore.Advanced.Serializer.ToJson(target);

            var version = Guid.NewGuid();

            theIdentityMap.Get<Target>(target.Id, json, version);

            theIdentityMap.Versions.Version<Target>(target.Id)
                .ShouldBe(version);
        }

        [Fact]
        public void get_by_id_and_json_and_type()
        {
            var target = Target.Random();
            var json = theStore.Advanced.Serializer.ToJson(target);

            var version = Guid.NewGuid();

            theIdentityMap.Get<Target>(target.Id, typeof(Target), json, version);

            theIdentityMap.Versions.Version<Target>(target.Id)
                .ShouldBe(version);
        }

        [Fact]
        public void get_with_sync_fetch()
        {
            var target = Target.Random();
            var json = theStore.Advanced.Serializer.ToJson(target);

            var version = Guid.NewGuid();

            theIdentityMap.Get<Target>(target.Id, () => new FetchResult<Target>(target, json, version));

            theIdentityMap.Versions.Version<Target>(target.Id)
                .ShouldBe(version);

        }

        [Fact]
        public async Task get_with_async_fetch()
        {
            var target = Target.Random();
            var json = theStore.Advanced.Serializer.ToJson(target);

            var version = Guid.NewGuid();

            await theIdentityMap.GetAsync(target.Id, tkn =>
            {
                return Task.FromResult(new FetchResult<Target>(target, json, version));
            }, default(CancellationToken));

            theIdentityMap.Versions.Version<Target>(target.Id)
                .ShouldBe(version);
        }
    }
}