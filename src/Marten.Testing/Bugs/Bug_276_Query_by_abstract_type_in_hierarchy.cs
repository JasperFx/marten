using System;
using Marten.Services;
using Xunit;
using Xunit.Abstractions;

namespace Marten.Testing.Bugs
{
    public class Bug_276_Query_by_abstract_type_in_hierarchy : DocumentSessionFixture<IdentityMap>
    {
        private readonly ITestOutputHelper _output;

        public Bug_276_Query_by_abstract_type_in_hierarchy(ITestOutputHelper output)
        {
            _output = output;
            StoreOptions(_ =>
            {
                _.Schema.For<Activity>()
                    .AddSubclass<StatusActivity>();
            });
        }

        public abstract class Activity
        {
            public Activity()
            {
                Id = Guid.NewGuid();
            }

            public Guid Id { get; set; }

            public abstract string Type { get; }
        }

        public class StatusActivity : Activity
        {
            public override string Type
            {
                get { return "StatusUpdate"; }
            }
            public string StatusText { get; set; }
        }

        [Fact]
        public void persist_and_load_subclass_with_abstract_parent()
        {
            var activity = new StatusActivity()
            {
                Id = Guid.NewGuid(),
                StatusText = "testing status"
            };

            theSession.Store(activity);
            theSession.SaveChanges();

            theSession.Load<Activity>(activity.Id).ShouldBeTheSameAs(activity);
            theSession.Load<StatusActivity>(activity.Id).ShouldBeTheSameAs(activity);

            using (var session = theStore.QuerySession())
            {
                session.Load<Activity>(activity.Id).ShouldNotBeTheSameAs(activity).ShouldNotBeNull();
                session.Load<StatusActivity>(activity.Id).ShouldNotBeTheSameAs(activity).ShouldNotBeNull();
            }
        }
    }
}