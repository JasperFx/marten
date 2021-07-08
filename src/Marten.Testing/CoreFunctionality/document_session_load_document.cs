using System;
using System.Threading;
using System.Threading.Tasks;
using Baseline.Dates;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.CoreFunctionality
{
    public class document_session_load_document: IntegrationContext
    {
        [Fact]
        public void when_id_setter_is_private()
        {
            var user = new UserWithPrivateId();

            theSession.Store(user);
            theSession.SaveChanges();

            user.Id.ShouldNotBe(Guid.Empty);

            var issue = theSession.Load<Issue>(user.Id);
            issue.ShouldBeNull();
        }

        [Fact]
        public void when_no_id_setter()
        {
            var user = new UserWithoutIdSetter();

            theSession.Store(user);
            theSession.SaveChanges();

            user.Id.ShouldBe(Guid.Empty);
        }

        [Fact]
        public async Task when_collectionstorage_asarray_and_with_readonlycollection_with_integers_and_private_setter()
        {
            var cancellation = new CancellationTokenSource();
            cancellation.CancelAfter(1.Minutes());

            // CancellationToken
            var token = cancellation.Token;


            StoreOptions(_ =>
            {
                _.UseDefaultSerialization(collectionStorage: CollectionStorage.AsArray);
            });

            var user = new UserWithReadonlyCollectionWithPrivateSetter(Guid.NewGuid(), "James", new[] { 1, 2, 3 });

            theSession.Store(user);
            await theSession.SaveChangesAsync(token);

            var userFromDb = await theSession.LoadAsync<UserWithReadonlyCollectionWithPrivateSetter>(user.Id, token);
            userFromDb.Id.ShouldBe(user.Id);
            userFromDb.Name.ShouldBe(user.Name);
            userFromDb.Collection.ShouldHaveTheSameElementsAs(user.Collection);
        }

        public document_session_load_document(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
