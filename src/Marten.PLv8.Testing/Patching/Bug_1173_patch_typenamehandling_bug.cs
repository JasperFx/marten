using System.Threading.Tasks;
using JasperFx;
using Marten.PLv8.Patching;
using Marten.Services;
using Marten.Testing.Harness;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;

namespace Marten.PLv8.Testing.Patching;

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
        using var store = SeparateStore(opts =>
        {
            var serializer = new JsonNetSerializer();
            serializer.Customize(config =>
            {
                config.TypeNameHandling = Newtonsoft.Json.TypeNameHandling.Objects;
            });
            opts.Serializer(serializer);
            opts.AutoCreateSchemaObjects = AutoCreate.All;

            opts.UseJavascriptTransformsAndPatching();
        });

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
            var expected = "{\"Id\": \"1\", \"$type\": \"Marten.PLv8.Testing.Patching.PatchTypeA, Marten.PLv8.Testing\", \"TypeB\": {\"Name\": \"test2\", \"$type\": \"Marten.PLv8.Testing.Patching.PatchTypeB, Marten.PLv8.Testing\"}}";
            Assert.Equal(expected, result);
        }
    }
}
