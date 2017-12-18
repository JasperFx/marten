using Marten.Services;
using System;
using System.Linq;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_932_or_query_in_inherited_class : DocumentSessionFixture<IdentityMap>
    {
        public Bug_932_or_query_in_inherited_class()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<Activity>()
                    .AddSubClass<StatusActivity>();
            });
        }

        public class Activity
        {
            public Activity()
            {
                Id = Guid.NewGuid();
            }

            public Guid Id { get; set; }
        }

        public class StatusActivity : Activity
        {
            public string StatusText { get; set; }
        }

        [Fact]
        public void query_with_or_in_base_class()
        {
            Guid refGuid = Guid.NewGuid();
            var activity = new Activity()
            {
                Id = refGuid
            };
            var activity2 = new Activity()
            {
                Id = Guid.NewGuid()
            };

            theSession.Store(activity, activity2);
            theSession.SaveChanges();

            int result = theSession.Query<Activity>().Where(x => x.Id == refGuid).Count();
            int result2 = theSession.Query<Activity>().Where(x => x.Id == refGuid || x.Id == refGuid).Count();

            Assert.Equal(result, result2);
        }

        [Fact]
        public void query_with_or_in_inherited_class()
        {
            Guid refGuid = Guid.NewGuid();
            var activity = new StatusActivity()
            {
                Id = refGuid
            };
            var activity2 = new StatusActivity()
            {
                Id = Guid.NewGuid()
            };

            theSession.Store(activity, activity2);
            theSession.SaveChanges();

            int result = theSession.Query<StatusActivity>().Where(x => x.Id == refGuid).Count();
            int result2 = theSession.Query<StatusActivity>().Where(x => x.Id == refGuid || x.Id == refGuid).Count();

            Assert.Equal(result, result2);
        }
    }
}
