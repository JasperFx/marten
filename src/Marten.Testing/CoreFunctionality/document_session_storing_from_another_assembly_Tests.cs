using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Xunit;

namespace Marten.Testing.CoreFunctionality
{
    public class document_session_storing_from_another_assembly_Tests : IntegrationContext
    {
        [Fact]
        public void store_document_inherited_from_document_with_id_from_another_assembly()
        {
            using (var session = theStore.OpenSession())
            {
                var user = new UserFromBaseDocument();
                session.Store(user);
                session.Load<UserFromBaseDocument>(user.Id).ShouldBeTheSameAs(user);
            }
        }

        public document_session_storing_from_another_assembly_Tests(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
