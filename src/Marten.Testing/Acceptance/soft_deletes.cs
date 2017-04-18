using System;
using System.Linq;
using Marten.Linq.SoftDeletes;
using Marten.Schema;
using Marten.Testing.Documents;
using Marten.Util;
using Shouldly;
using Xunit;
using System.Collections.Generic;

namespace Marten.Testing.Acceptance
{
    public class soft_deletes : IntegratedFixture
    {

        public soft_deletes()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<User>().SoftDeletedWithIndex();
                _.Schema.For<File>().SoftDeleted();
            });
        }

        [Fact]
        public void soft_deleted_documents_have_the_extra_deleted_columns()
        {
            theStore.DefaultTenant.EnsureStorageExists(typeof(User));

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
            var user1 = new User { UserName = "foo" };
            var user2 = new User { UserName = "bar" };
            var user3 = new User { UserName = "baz" };

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

        // SAMPLE: query_soft_deleted_docs
        [Fact]
        public void query_soft_deleted_docs()
        {
            var user1 = new User { UserName = "foo" };
            var user2 = new User { UserName = "bar" };
            var user3 = new User { UserName = "baz" };
            var user4 = new User { UserName = "jack" };

            using (var session = theStore.OpenSession())
            {
                session.Store(user1, user2, user3, user4);
                session.SaveChanges();

                // Deleting 'bar' and 'baz'
                session.DeleteWhere<User>(x => x.UserName.StartsWith("b"));
                session.SaveChanges();

                // no where clause, deleted docs should be filtered out
                session.Query<User>().OrderBy(x => x.UserName).Select(x => x.UserName)
                    .ToList().ShouldHaveTheSameElementsAs("foo", "jack");

                // with a where clause
                session.Query<User>().Where(x => x.UserName != "jack")
                .ToList().Single().UserName.ShouldBe("foo");
            }
        }
        // ENDSAMPLE

        // SAMPLE: query_maybe_soft_deleted_docs
        [Fact]
        public void query_maybe_soft_deleted_docs()
        {
            var user1 = new User { UserName = "foo" };
            var user2 = new User { UserName = "bar" };
            var user3 = new User { UserName = "baz" };
            var user4 = new User { UserName = "jack" };

            using (var session = theStore.OpenSession())
            {
                session.Store(user1, user2, user3, user4);
                session.SaveChanges();

                session.DeleteWhere<User>(x => x.UserName.StartsWith("b"));
                session.SaveChanges();

                // no where clause, all documents are returned
                session.Query<User>().Where(x => x.MaybeDeleted()).OrderBy(x => x.UserName).Select(x => x.UserName)
                    .ToList().ShouldHaveTheSameElementsAs("bar", "baz", "foo", "jack");

                // with a where clause, all documents are returned
                session.Query<User>().Where(x => x.UserName != "jack" && x.MaybeDeleted())
                    .OrderBy(x => x.UserName)
                    .ToList()
                    .Select(x => x.UserName)
                    .ShouldHaveTheSameElementsAs("bar", "baz", "foo");
            }
        }
        // ENDSAMPLE

        // SAMPLE: query_is_soft_deleted_docs
        [Fact]
        public void query_is_soft_deleted_docs()
        {
            var user1 = new User { UserName = "foo" };
            var user2 = new User { UserName = "bar" };
            var user3 = new User { UserName = "baz" };
            var user4 = new User { UserName = "jack" };

            using (var session = theStore.OpenSession())
            {
                session.Store(user1, user2, user3, user4);
                session.SaveChanges();

                session.DeleteWhere<User>(x => x.UserName.StartsWith("b"));
                session.SaveChanges();

                // no where clause
                session.Query<User>().Where(x => x.IsDeleted()).OrderBy(x => x.UserName).Select(x => x.UserName)
                    .ToList().ShouldHaveTheSameElementsAs("bar", "baz");

                // with a where clause
                session.Query<User>().Where(x => x.UserName != "baz" && x.IsDeleted())
                    .OrderBy(x => x.UserName)
                    .ToList()
                    .Select(x => x.UserName)
                    .Single().ShouldBe("bar");
            }
        }
        // ENDSAMPLE

        // SAMPLE: query_soft_deleted_since
        [Fact]
        public void query_is_soft_deleted_since_docs()
        {
            var user1 = new User { UserName = "foo" };
            var user2 = new User { UserName = "bar" };
            var user3 = new User { UserName = "baz" };
            var user4 = new User { UserName = "jack" };

            using (var session = theStore.OpenSession())
            {
                session.Store(user1, user2, user3, user4);
                session.SaveChanges();

                session.Delete(user3);
                session.SaveChanges();

                var epoch = session.DocumentStore.Advanced.MetadataFor(user3).DeletedAt;
                session.Delete(user4);
                session.SaveChanges();

                session.Query<User>().Where(x => x.DeletedSince(epoch.Value)).Select(x => x.UserName)
                    .ToList().ShouldHaveTheSameElementsAs("jack");
            }
        }
        // ENDSAMPLE

        [Fact]
        public void query_is_soft_deleted_before_docs()
        {
            var user1 = new User { UserName = "foo" };
            var user2 = new User { UserName = "bar" };
            var user3 = new User { UserName = "baz" };
            var user4 = new User { UserName = "jack" };

            using (var session = theStore.OpenSession())
            {
                session.Store(user1, user2, user3, user4);
                session.SaveChanges();

                session.Delete(user3);
                session.SaveChanges();

                session.Delete(user4);
                session.SaveChanges();

                var epoch = session.DocumentStore.Advanced.MetadataFor(user4).DeletedAt;

                session.Query<User>().Where(x => x.DeletedBefore(epoch.Value)).Select(x => x.UserName)
                    .ToList().ShouldHaveTheSameElementsAs("baz");
            }
        }

        [Fact]
        public void top_level_of_hierarchy()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<User>()
                    .SoftDeleted()
                    .AddSubClass<AdminUser>()
                    .AddSubClass<SuperUser>();
            });

            var user1 = new User { UserName = "foo" };
            var user2 = new User { UserName = "bar" };
            var user3 = new User { UserName = "baz" };
            var user4 = new User { UserName = "jack" };

            using (var session = theStore.OpenSession())
            {
                session.Store(user1, user2, user3, user4);
                session.SaveChanges();

                session.DeleteWhere<User>(x => x.UserName.StartsWith("b"));
                session.SaveChanges();

                // no where clause
                session.Query<User>().OrderBy(x => x.UserName).Select(x => x.UserName)
                    .ToList().ShouldHaveTheSameElementsAs("foo", "jack");

                // with a where clause
                session.Query<User>().Where(x => x.UserName != "jack")
                    .ToList().Single().UserName.ShouldBe("foo");
            }
        }


        [Fact]
        public void sub_level_of_hierarchy()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<User>()
                    .SoftDeleted()
                    .AddSubClass<AdminUser>()
                    .AddSubClass<SuperUser>();
            });

            var user1 = new SuperUser { UserName = "foo" };
            var user2 = new SuperUser { UserName = "bar" };
            var user3 = new SuperUser { UserName = "baz" };
            var user4 = new SuperUser { UserName = "jack" };
            var user5 = new AdminUser { UserName = "admin" };

            using (var session = theStore.OpenSession())
            {
                session.StoreObjects(new User[] { user1, user2, user3, user4, user5 });
                session.SaveChanges();

                session.DeleteWhere<SuperUser>(x => x.UserName.StartsWith("b"));
                session.SaveChanges();

                // no where clause
                session.Query<SuperUser>().OrderBy(x => x.UserName).Select(x => x.UserName)
                    .ToList().ShouldHaveTheSameElementsAs("foo", "jack");

                // with a where clause
                session.Query<SuperUser>().Where(x => x.UserName != "jack")
                    .ToList().Single().UserName.ShouldBe("foo");
            }
        }

        [Fact]
        public void sub_level_of_hierarchy_maybe_deleted()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<User>()
                    .SoftDeleted()
                    .AddSubClass<AdminUser>()
                    .AddSubClass<SuperUser>();
            });

            var user1 = new SuperUser { UserName = "foo" };
            var user2 = new SuperUser { UserName = "bar" };
            var user3 = new SuperUser { UserName = "baz" };
            var user4 = new SuperUser { UserName = "jack" };
            var user5 = new AdminUser { UserName = "admin" };

            using (var session = theStore.OpenSession())
            {
                session.StoreObjects(new User[] { user1, user2, user3, user4, user5 });
                session.SaveChanges();

                session.DeleteWhere<SuperUser>(x => x.UserName.StartsWith("b"));
                session.SaveChanges();

                // no where clause
                session.Query<SuperUser>().Where(x => x.MaybeDeleted()).OrderBy(x => x.UserName).Select(x => x.UserName)
                    .ToList().ShouldHaveTheSameElementsAs("bar", "baz", "foo", "jack");

                // with a where clause
                session.Query<SuperUser>().Where(x => x.UserName != "jack" && x.MaybeDeleted())
                    .OrderBy(x => x.UserName)
                    .Select(x => x.UserName)
                    .ToList().ShouldHaveTheSameElementsAs("bar", "baz", "foo");
            }
        }


        [Fact]
        public void sub_level_of_hierarchy_is_deleted()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<User>()
                    .SoftDeleted()
                    .AddSubClass<AdminUser>()
                    .AddSubClass<SuperUser>();
            });

            var user1 = new SuperUser { UserName = "foo" };
            var user2 = new SuperUser { UserName = "bar" };
            var user3 = new SuperUser { UserName = "baz" };
            var user4 = new SuperUser { UserName = "jack" };
            var user5 = new AdminUser { UserName = "admin" };

            using (var session = theStore.OpenSession())
            {
                session.StoreObjects(new User[] { user1, user2, user3, user4, user5 });
                session.SaveChanges();

                session.DeleteWhere<SuperUser>(x => x.UserName.StartsWith("b"));
                session.SaveChanges();

                // no where clause
                session.Query<SuperUser>().Where(x => x.IsDeleted()).OrderBy(x => x.UserName).Select(x => x.UserName)
                    .ToList().ShouldHaveTheSameElementsAs("bar", "baz");

                // with a where clause
                session.Query<SuperUser>().Where(x => x.UserName != "bar" && x.IsDeleted())
                    .OrderBy(x => x.UserName)
                    .Select(x => x.UserName)
                    .ToList().ShouldHaveTheSameElementsAs("baz");
            }
        }

        [Fact]
        public void soft_deleted_documents_work_with_linq_include()
        {
            theStore.DefaultTenant.EnsureStorageExists(typeof(User));
            theStore.DefaultTenant.EnsureStorageExists(typeof(File));

            using (var session = theStore.OpenSession())
            {
                var user = new User();
                session.Store(user);
                var file1 = new File() { UserId = user.Id };
                session.Store(file1);
                var file2 = new File() { UserId = user.Id };
                session.Store(file2);
                session.SaveChanges();
                session.Delete(file2);
                session.SaveChanges();

                var users = new List<User>();
                var files = session.Query<File>().Include(u => u.UserId, users).ToList();
                files.Count.ShouldBe(1);
                users.Count.ShouldBe(1);
                files = session.Query<File>().Where(f => f.MaybeDeleted()).Include(u => u.UserId, users).ToList();
                files.Count.ShouldBe(2);
                files = session.Query<File>().Where(f => f.IsDeleted()).Include(u => u.UserId, users).ToList();
                files.Count.ShouldBe(1);
            }
        }

        public class File
        {
            public Guid Id { get; set; } = Guid.NewGuid();
            public Guid UserId { get; set; }
            public string Path { get; set; }
        }
    }
}
