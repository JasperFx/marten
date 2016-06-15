using System;
using Marten.Schema;
using Marten.Testing.Documents;
using Marten.Testing.Fixtures;
using Marten.Util;
using Xunit;

namespace Marten.Testing.Acceptance
{
    public class soft_deletes : IntegratedFixture
    {
        public soft_deletes()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<User>().SoftDeleted();
            });
        }

        [Fact]
        public void soft_deleted_documents_have_the_extra_deleted_columns()
        {
            theStore.Schema.EnsureStorageExists(typeof(User));

            var table = theStore.Schema.DbObjects.TableSchema(typeof(User));
            table.HasColumn(DocumentMapping.DeletedColumn);
            table.HasColumn(DocumentMapping.DeletedAtColumn);
        }

        [Fact]
        public void initial_state_of_deleted_columns()
        {
            using (var session = theStore.OpenSession())
            {

                var user = new User();
                session.Store(user);
                session.SaveChanges();

                userIsNotMarkedAsDeleted(session, user.Id);
            }
        }

        private static void userIsNotMarkedAsDeleted(IDocumentSession session, Guid userId)
        {
            var cmd = session.Connection.CreateCommand()
                .Sql("select mt_deleted, mt_deleted_at from public.mt_doc_user where id = :id")
                .With("id", userId);

            using (var reader = cmd.ExecuteReader())
            {
                reader.Read();

                reader.GetFieldValue<bool>(0).ShouldBeFalse();
                reader.IsDBNull(1).ShouldBeTrue();
            }
        }

        [Fact]
        public void soft_delete_a_document_row_state()
        {
            using (var session = theStore.OpenSession())
            {

                var user = new User();
                session.Store(user);
                session.SaveChanges();

                session.Delete(user);
                session.SaveChanges();

                userIsMarkedAsDeleted(session, user.Id);
            }
        }

        private static void userIsMarkedAsDeleted(IDocumentSession session, Guid userId)
        {
            var cmd = session.Connection.CreateCommand()
                .Sql("select mt_deleted, mt_deleted_at from public.mt_doc_user where id = :id")
                .With("id", userId);

            using (var reader = cmd.ExecuteReader())
            {
                reader.Read();

                reader.GetFieldValue<bool>(0).ShouldBeTrue();
                reader.IsDBNull(1).ShouldBeFalse();
            }
        }

        [Fact]
        public void soft_delete_a_document_by_where_row_state()
        {
            var user1 = new User {UserName = "foo"};
            var user2 = new User {UserName = "bar"};
            var user3 = new User {UserName = "baz"};

            using (var session = theStore.OpenSession())
            {
                session.Store(user1, user2, user3);
                session.SaveChanges();

                session.DeleteWhere<User>(x => x.UserName.StartsWith("b"));
                session.SaveChanges();

                userIsNotMarkedAsDeleted(session, user1.Id);
                userIsMarkedAsDeleted(session, user2.Id);
                userIsMarkedAsDeleted(session, user3.Id);
            }
        }
    }
}