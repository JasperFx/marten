using System;
using Marten.Services;
using Xunit;

namespace Marten.Testing.Bugs
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

    public class Bug_1173_patch_typenamehandling_bug : DocumentSessionFixture<NulloIdentityMap>
    {
        [Fact]
        public void can_support_typenamehandling()
        {
            using (var store = DocumentStore.For(_ =>
            {
                var serializer = new JsonNetSerializer();
                serializer.Customize(config =>
                {
                    config.TypeNameHandling = Newtonsoft.Json.TypeNameHandling.Objects;
                });
                _.Serializer(serializer);
                _.AutoCreateSchemaObjects = Marten.AutoCreate.All;
                _.Connection(ConnectionSource.ConnectionString);
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
                    var expected = "{\"Id\": \"1\", \"$type\": \"Marten.Testing.Bugs.PatchTypeA, Marten.Testing\", \"TypeB\": {\"Name\": \"test2\", \"$type\": \"Marten.Testing.Bugs.PatchTypeB, Marten.Testing\"}}";
                    Assert.Equal(expected, result);
                }
            }
        }
    }
}