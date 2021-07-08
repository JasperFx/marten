using System.Linq;
using System.Threading.Tasks;
using Marten.Services;
using Marten.Testing.Harness;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;

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

    public class Bug_1061_string_enum_serialization_does_not_work_with_ginindexjsondata: IntegrationContext
    {
        [Fact]
        public async Task string_enum_serialization_does_not_work_with_ginindexjsondata()
        {

            StoreOptions(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;
                _.Advanced.DdlRules.TableCreation = CreationStyle.CreateIfNotExists;

                _.Schema.For<Bug_1061_Class>().GinIndexJsonData(_ =>
                {
                    _.Columns = new []{"to_tsvector('english', data::TEXT)"};
                });
            });

            await theStore.Schema.ApplyAllConfiguredChangesToDatabase();

            using var store = DocumentStore.For(_ =>
            {
                _.Serializer(new JsonNetSerializer
                {
                    EnumStorage = EnumStorage.AsString,
                    Casing = Casing.Default
                });
                _.Connection(ConnectionSource.ConnectionString);
                _.Schema.For<Bug_1061_Class>().GinIndexJsonData(_ =>
                {
                    _.Columns = new []{"to_tsvector('english', data::TEXT)"};
                });
            });

            using (var session = store.OpenSession())
            {
                session.Store(new Bug_1061_Class { Id = "one", Enum = Bug_1061_Enum.One });
                await session.SaveChangesAsync();
            }

            using (var session = store.OpenSession())
            {
                var items = session.Query<Bug_1061_Class>().Where(x => x.Enum == Bug_1061_Enum.One).ToList();
                Assert.Single(items);
            }
        }

        public Bug_1061_string_enum_serialization_does_not_work_with_ginindexjsondata(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
