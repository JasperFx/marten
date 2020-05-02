using System;
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
        public void when_collectionstorage_asarray_and_with_readonlycollection_with_integers_and_private_setter()
        {
            StoreOptions(_ =>
            {
                _.UseDefaultSerialization(collectionStorage: CollectionStorage.AsArray);
            });

            var user = new UserWithReadonlyCollectionWithPrivateSetter(Guid.NewGuid(), "James", new[] { 1, 2, 3 });

            theSession.Store(user);
            theSession.SaveChanges();

            var userFromDb = theSession.Load<UserWithReadonlyCollectionWithPrivateSetter>(user.Id);
            userFromDb.Id.ShouldBe(user.Id);
            userFromDb.Name.ShouldBe(user.Name);
            userFromDb.Collection.ShouldHaveTheSameElementsAs(user.Collection);
        }

        public document_session_load_document(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
