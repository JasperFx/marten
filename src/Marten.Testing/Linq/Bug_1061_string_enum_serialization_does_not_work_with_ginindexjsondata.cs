using Xunit;
using Marten.Services;
using System.Linq;

namespace Marten.Testing.Linq
{
    public class Bug_1061_Class
    {
        public string Id { get; set; }
        public Bug_1061_Enum Enum { get; set; }
    }

    public enum Bug_1061_Enum
    {
        One
    }

    public class Bug_1061_Registry : MartenRegistry
    {
        public Bug_1061_Registry()
        {
            For<Bug_1061_Class>().GinIndexJsonData(_ =>
            {
                _.Expression = "to_tsvector('english', data::TEXT)";
            });
        }
    }

    public class Bug_1061_string_enum_serialization_does_not_work_with_ginindexjsondata : IntegratedFixture
    {
        [Fact]
        public void string_enum_serialization_does_not_work_with_ginindexjsondata()
        {
            EnableCommandLogging = true;

            StoreOptions(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;
                _.DdlRules.TableCreation = CreationStyle.CreateIfNotExists;
                _.Schema.Include(new Bug_1061_Registry());
            });

            theStore.Schema.ApplyAllConfiguredChangesToDatabase();

            using (var store = DocumentStore.For(_ =>
            {
                _.Serializer(new JsonNetSerializer
                {
                    EnumStorage = EnumStorage.AsString,
                    Casing = Casing.Default
                });
                _.Connection(ConnectionSource.ConnectionString);
                _.Schema.Include(new Bug_1061_Registry());
            }))
            {
                using (var session = store.OpenSession())
                {
                    session.Store(new Bug_1061_Class { Id = "one", Enum = Bug_1061_Enum.One });
                    session.SaveChanges();
                }
                using (var session = store.OpenSession())
                {
                    var items = session.Query<Bug_1061_Class>().Where(x => x.Enum == Bug_1061_Enum.One).ToList();
                    Assert.Single(items);
                }
            }
        }
    }
}
