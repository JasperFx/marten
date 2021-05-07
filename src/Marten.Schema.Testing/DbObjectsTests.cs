using System.Linq;
using System.Threading.Tasks;
using Baseline;
using Weasel.Postgresql;
using Marten.Schema.Testing.Documents;
using Shouldly;
using Xunit;

namespace Marten.Schema.Testing
{
    [Collection("dbobjects")]
    public class DbObjectsTests : IntegrationContext
    {

        [Fact]
        public async Task can_fetch_the_function_ddl()
        {
            var store1 = StoreOptions(_ =>
            {
                _.DatabaseSchemaName = "other";
                _.Schema.For<User>().Duplicate(x => x.UserName).Duplicate(x => x.Internal);
            });

            store1.Tenancy.Default.EnsureStorageExists(typeof(User));

            var upsert = store1.Storage.MappingFor(typeof(User)).As<DocumentMapping>().UpsertFunction;

            var functionBody = await store1.Tenancy.Default.DefinitionForFunction(upsert);

            functionBody.Body().ShouldContain( "mt_doc_user");
        }

    }
}
