using System.Linq;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Xunit;

namespace Marten.Testing.Services
{
    public class JsonNetSerializerTests : IntegrationContext
    {
        [Fact]
        public void deserialized_stored_object()
        {
            var document = Target.Random();

            theSession.Insert(document);
            theSession.SaveChanges();

            var documentFromDb = theSession.Query<Target>().Single(d => d.Id == document.Id);

            documentFromDb.ShouldNotBeNull();;
        }

        public JsonNetSerializerTests(DefaultStoreFixture fixture) : base(fixture)
        {
            StoreOptions(opt => opt.DatabaseSchemaName = "json_net_serializer");
        }
    }
}
