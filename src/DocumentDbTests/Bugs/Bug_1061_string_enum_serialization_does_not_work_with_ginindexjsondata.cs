using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Services;
using Marten.Testing.Harness;
using Weasel.Core;
using Xunit;

namespace DocumentDbTests.Bugs;

public class Bug_1061_Class
{
    public string Id { get; set; }
    public Bug_1061_Enum Enum { get; set; }
}

public enum Bug_1061_Enum
{
    One
}

public class Bug_1061_string_enum_serialization_does_not_work_with_ginindexjsondata : BugIntegrationContext
{
    [Fact]
    public async Task string_enum_serialization_does_not_work_with_ginindexjsondata()
    {
        using var store = StoreOptions(opts =>
        {
            opts.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;
            opts.Advanced.Migrator.TableCreation = CreationStyle.CreateIfNotExists;
            opts.Connection(ConnectionSource.ConnectionString);

            opts.DatabaseSchemaName = "Bug1061";

            opts.Schema.For<Bug_1061_Class>().GinIndexJsonData(_ =>
            {
                _.Columns = new[] { "to_tsvector('english', data::TEXT)" };
            });
        });

        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        using var store2 = SeparateStore(_ =>
        {
            _.Serializer(new JsonNetSerializer { EnumStorage = EnumStorage.AsString, Casing = Casing.Default });
            _.Connection(ConnectionSource.ConnectionString);
            _.Schema.For<Bug_1061_Class>().GinIndexJsonData(_ =>
            {
                _.Columns = new[] { "to_tsvector('english', data::TEXT)" };
            });
        });

        await using (var session = store2.LightweightSession())
        {
            session.Store(new Bug_1061_Class { Id = "one", Enum = Bug_1061_Enum.One });
            await session.SaveChangesAsync();
        }

        await using (var session = store2.LightweightSession())
        {
            var items = session.Query<Bug_1061_Class>().Where(x => x.Enum == Bug_1061_Enum.One).ToList();
            Assert.Single(items);
        }
    }

}
