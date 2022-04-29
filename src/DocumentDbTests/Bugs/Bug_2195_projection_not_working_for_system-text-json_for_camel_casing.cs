using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Schema.Identity.Sequences;
using Marten.Services;
using Marten.Testing.Harness;
using Weasel.Core;
using Xunit;
using Shouldly;

namespace DocumentDbTests.Bugs
{
    public class Bug_2195_projection_not_working_for_system_text_json_for_camel_casing : BugIntegrationContext
    {
        [Fact]
        public async Task Collection_Should_Populate_Correctly_For_Camel_Casing()
        {
            StoreOptions(options =>
            {
                options.Serializer(new SystemTextJsonSerializer { EnumStorage = EnumStorage.AsString, Casing = Casing.CamelCase });
                options.Schema.For<TestObject2195>().HiloSettings(new HiloSettings {MaxLo = 10});
            });

            var newObject = new TestObject2195 { Name = "Banana", Value = 12345 };

            await using (var session = theStore.LightweightSession())
            {
                session.Store(newObject);
                await session.SaveChangesAsync();
            }

            await using (var session = theStore.LightweightSession())
            {
                var result = await session.Query<TestObject2195>()
                    .Where(x => x.Name == "Banana")
                    .Select(x => new ProjectionTestObject2195 { Name = x.Name, Value = x.Value }).SingleAsync();

                result.ShouldNotBeNull();
                result.Name.ShouldBe("Banana");
                result.Value.ShouldBe(12345);
            }
        }
    }

    public class ProjectionTestObject2195
    {
        public string Name { get; set; }

        public int Value { get; set; }
    }

    public class TestObject2195
    {
        public int Id { get; set; }

        public string Name { get; set; }

        public int Value { get; set; }
    }
}
