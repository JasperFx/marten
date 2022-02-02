using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Reading.Loading
{

    public class document_session_load_already_loaded_document_Tests : IntegrationContext
    {
        [Fact]
        public void when_loading_then_a_different_document_should_be_returned()
        {
            var user = new User { FirstName = "Tim", LastName = "Cools" };
            theSession.Store(user);
            theSession.SaveChanges();

            using var session = theStore.OpenSession();
            var first = session.Load<User>(user.Id);
            var second = session.Load<User>(user.Id);

            first.ShouldBeSameAs(second);
        }

        [Fact]
        public async Task when_loading_then_a_different_document_should_be_returned_async()
        {
            var user = new User { FirstName = "Tim", LastName = "Cools" };
            theSession.Store(user);
            await theSession.SaveChangesAsync();

            using var session = theStore.OpenSession();
            var first = await session.LoadAsync<User>(user.Id);
            var second = await session.LoadAsync<User>(user.Id);

            first.ShouldBeSameAs(second);
        }



        public document_session_load_already_loaded_document_Tests(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
