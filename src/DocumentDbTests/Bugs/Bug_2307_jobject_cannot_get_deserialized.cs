namespace DocumentDbTests.Bugs
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Marten;
    using Marten.Exceptions;
    using Marten.Testing.Harness;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using Shouldly;
    using Xunit;

    public class Bug_2307_jobject_cannot_get_deserialized : BugIntegrationContext
    {
        [Fact]
        public async Task can_query_and_deserialize_jobject_with_select()
        {
            StoreOptions(opts =>
            {
                opts.Schema.For<MyModel>().Identity(x => x.Id);
                opts.UseDefaultSerialization();
            });
            var model1 = new MyModel
            {
                Id = 1, Status = "Check", InstanceData = JObject.FromObject(new { Data = "Pew Pew", }),
            };

            using (var session = theStore.LightweightSession())
            {
                session.Store(model1);
                await session.SaveChangesAsync();
            }

            using (var session = theStore.QuerySession())
            {
                var query = session.Query<MyModel>();
                var selection = query
                    .Where(x => x.Id == 1);
                var projection = selection
                    .Select(x => new { x.Status, x.Id , x.InstanceData, });
                var result = projection
                    .Single();


                result.ShouldSatisfyAllConditions(
                    () => result.Status.ShouldBe("Check"),
                    () => result.InstanceData["Data"].ShouldBe("Pew Pew"),
                    () => result.Id.ShouldBe(1)
                );
            }
        }

        [Fact]
        public async Task can_query_and_deserialize_jobject_without_select()
        {
            StoreOptions(opts =>
            {
                opts.Schema.For<MyModel>().Identity(x => x.Id);
                opts.UseDefaultSerialization();
            });
            var model1 = new MyModel
            {
                Id = 1, Status = "Check", InstanceData = JObject.FromObject(new { Data = "Pew Pew", }),
            };

            using (var session = theStore.LightweightSession())
            {
                session.Store(model1);
                await session.SaveChangesAsync();
            }

            using (var session = theStore.QuerySession())
            {
                var result = await session.Query<MyModel>()
                    .Where(x => x.Id == 1)
                    .SingleAsync();


                result.ShouldSatisfyAllConditions(
                    () => result.Status.ShouldBe("Check"),
                    () => result.Id.ShouldBe(1),
                    () => result.InstanceData["Data"].ShouldBe("Pew Pew")
                );
            }
        }

        public class MyModel
        {
            public int Id { get; set; }
            public string Status { get; set; }
            public JObject InstanceData { get; set; }
        }

    }
}
