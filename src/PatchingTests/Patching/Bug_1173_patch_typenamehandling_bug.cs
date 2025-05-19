using System.Threading.Tasks;
using JasperFx;
using Marten.Patching;
using Marten.Services;
using Marten.Testing.Harness;
using Weasel.Core;
using Xunit;

namespace PatchingTests.Patching;

public class PatchTypeA
{
    public string Id { get; set; }
    public PatchTypeB TypeB { get; set; }
}

public class PatchTypeB
{
    public string Name { get; set; }
}

public class Bug_1173_patch_typenamehandling_bug: BugIntegrationContext
{
    [Fact]
    public async Task can_support_typenamehandling()
    {
        using var store = SeparateStore(_ =>
        {
            var serializer = new JsonNetSerializer();
            serializer.Configure(config =>
            {
                config.TypeNameHandling = Newtonsoft.Json.TypeNameHandling.Objects;
            });
            _.Serializer(serializer);
            _.AutoCreateSchemaObjects = AutoCreate.All;
        });

        // store.Storage.ApplyAllConfiguredChangesToDatabaseAsync().Wait();

        using (var session = store.LightweightSession())
        {
            var obj = new PatchTypeA
            {
                Id = "1",
                TypeB =
                    new PatchTypeB
                    {
                        Name = "test"
                    }
            };

            session.Store(obj);
            await session.SaveChangesAsync();
        }
        using (var session = store.LightweightSession())
        {
            var newObj = new PatchTypeB
            {
                Name = "test2"
            };

            session.Patch<PatchTypeA>("1").Set(set => set.TypeB, newObj);
            await session.SaveChangesAsync();
        }

        using (var session = store.LightweightSession())
        {
            var result = await session.Json.FindByIdAsync<PatchTypeA>("1");
            var expected = "{\"Id\": \"1\", \"$type\": \"PatchingTests.Patching.PatchTypeA, PatchingTests\", \"TypeB\": {\"Name\": \"test2\", \"$type\": \"PatchingTests.Patching.PatchTypeB, PatchingTests\"}}";
            Assert.Equal(expected, result);
        }
    }
}
