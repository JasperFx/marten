using Marten.PLv8.Patching;
using Marten.Services;
using Marten.Testing.Harness;
using Weasel.Postgresql;
using Xunit;

namespace Marten.PLv8.Testing.Patching
{
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
        public void can_support_typenamehandling()
        {
            using (var store = SeparateStore(_ =>
            {
                var serializer = new JsonNetSerializer();
                serializer.Customize(config =>
                {
                    config.TypeNameHandling = Newtonsoft.Json.TypeNameHandling.Objects;
                });
                _.Serializer(serializer);
                _.AutoCreateSchemaObjects = AutoCreate.All;

                _.UseJavascriptTransformsAndPatching();
            }))
            {
                using (var session = store.OpenSession())
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
                    session.SaveChanges();
                }
                using (var session = store.OpenSession())
                {
                    var newObj = new PatchTypeB
                    {
                        Name = "test2"
                    };

                    session.Patch<PatchTypeA>("1").Set(set => set.TypeB, newObj);
                    session.SaveChanges();
                }

                using (var session = store.OpenSession())
                {
                    var result = session.Json.FindById<PatchTypeA>("1");
                    var expected = "{\"Id\": \"1\", \"$type\": \"Marten.PLv8.Testing.Patching.PatchTypeA, Marten.PLv8.Testing\", \"TypeB\": {\"Name\": \"test2\", \"$type\": \"Marten.PLv8.Testing.Patching.PatchTypeB, Marten.PLv8.Testing\"}}";
                    Assert.Equal(expected, result);
                }
            }
        }


    }
}
