using System;
using Marten.Services;
using Marten.Testing.Documents;
using Shouldly;
using Xunit;

namespace Marten.Testing.Session
{
    public class document_session_load_document: DocumentSessionFixture<NulloIdentityMap>
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
        public void when_collection_with_no_setter()
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
    }
}
