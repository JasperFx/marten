using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten.Linq.SoftDeletes;
using Marten.Metadata;
using Weasel.Postgresql;
using Marten.Testing.CoreFunctionality;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Marten.Testing.Internals;
using Marten.Util;
using Shouldly;
using Weasel.Core;
using Xunit;
using Xunit.Abstractions;

namespace Marten.Testing.Acceptance
{
    public class SoftDeletedFixture: StoreFixture
    {
        public SoftDeletedFixture() : base("softdelete")
        {
            Options.Schema.For<User>().SoftDeletedWithIndex();
            Options.Schema.For<File>().SoftDeleted()
                .Metadata(m => m.IsSoftDeleted.MapTo(x => x.Deleted));


            Options.Schema.For<User>()
                .SoftDeleted()
                .AddSubClass<AdminUser>()
                .AddSubClass<SuperUser>();

            Options.Schema.For<IntDoc>().SoftDeleted().MultiTenanted();
            Options.Schema.For<LongDoc>().SoftDeleted().MultiTenanted();
            Options.Schema.For<StringDoc>().SoftDeleted().MultiTenanted();
            Options.Schema.For<GuidDoc>().SoftDeleted().MultiTenanted();

        }
    }

    public class SoftDeletedDocument: ISoftDeleted
    {
        public string Id { get; set; }
        public bool Deleted { get; set; }
        public DateTimeOffset? DeletedAt { get; set; }
    }

    public class soft_deletes: StoreContext<SoftDeletedFixture>, IClassFixture<SoftDeletedFixture>
    {
        private readonly ITestOutputHelper _output;

        public soft_deletes(SoftDeletedFixture fixture, ITestOutputHelper output): base(fixture)
        {
            _output = output;
            theStore.Advanced.Clean.DeleteAllDocuments();
        }

        [Fact]
        public async Task can_query_by_the_deleted_column_if_it_exists()
        {
            var doc1 = new SoftDeletedDocument{Id = "1"};
            var doc2 = new SoftDeletedDocument{Id = "2"};
            var doc3 = new SoftDeletedDocument{Id = "3"};
            var doc4 = new SoftDeletedDocument{Id = "4"};
            var doc5 = new SoftDeletedDocument{Id = "5"};

            theSession.Store(doc1, doc2, doc3, doc4, doc5);
            await theSession.SaveChangesAsync();

            var session2 = theStore.LightweightSession();
            session2.Delete(doc1);
            session2.Delete(doc3);
            await session2.SaveChangesAsync();

            var query = theStore.QuerySession();
            query.Logger = new TestOutputMartenLogger(_output);

            var deleted = await query.Query<SoftDeletedDocument>().Where(x => x.Deleted)
                .CountAsync();

            var notDeleted = await query.Query<SoftDeletedDocument>().Where(x => !x.Deleted)
                .CountAsync();

            deleted.ShouldBe(2);
            notDeleted.ShouldBe(3);
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
            var cmd = session.Connection.CreateCommand("select mt_deleted, mt_deleted_at from softdelete.mt_doc_user where id = :id")
                .With("id", userId);

            using (var reader = cmd.ExecuteReader())
            {
                reader.Read();

                reader.GetFieldValue<bool>(0).ShouldBeFalse();
                reader.IsDBNull(1).ShouldBeTrue();
            }
        }

        private void assertDocumentIsHardDeleted<T>(IDocumentSession session, object id)
        {
            var mapping = theStore.Options.Storage.MappingFor(typeof(T));


            var cmd = session.Connection.CreateCommand($"select count(*) from {mapping.TableName} where id = :id")
                .With("id", id);

            var count = (long)cmd.ExecuteScalar();
            count.ShouldBe(0);
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

        [Fact]
        public void hard_delete_a_document_row_state()
        {
            using var session = theStore.OpenSession();
            var user = new User();
            session.Store(user);
            session.SaveChanges();

            session.HardDelete(user);
            session.SaveChanges();

            assertDocumentIsHardDeleted<User>(session, user.Id);
        }

        [Fact]
        public async Task hard_delete_by_linq()
        {
            var doc1 = new StringDoc {Id = "red", Size = "big"};
            var doc2 = new StringDoc {Id = "blue", Size = "small"};
            var doc3 = new StringDoc {Id = "green", Size = "big"};
            var doc4 = new StringDoc {Id = "purple", Size = "medium"};

            theSession.Store(doc1, doc2, doc3, doc4);
            await theSession.SaveChangesAsync();

            theSession.HardDeleteWhere<StringDoc>(x => x.Size == "big");
            await theSession.SaveChangesAsync();

            assertDocumentIsHardDeleted<StringDoc>(theSession,"red");
            assertDocumentIsHardDeleted<StringDoc>(theSession, "green");

            var count = await theSession.Query<StringDoc>().Where(x => x.MaybeDeleted()).CountAsync();
            count.ShouldBe(2);
        }

        [Fact]
        public void hard_delete_a_document_row_state_int()
        {
            using var session = theStore.OpenSession();
            var doc = new IntDoc();
            session.Store(doc);
            session.SaveChanges();

            session.HardDelete<IntDoc>(doc.Id);
            session.SaveChanges();

            assertDocumentIsHardDeleted<IntDoc>(session, doc.Id);
        }

        [Fact]
        public void hard_delete_a_document_row_state_long()
        {
            using var session = theStore.OpenSession();
            var doc = new LongDoc();
            session.Store(doc);
            session.SaveChanges();

            session.HardDelete<LongDoc>(doc.Id);
            session.SaveChanges();

            assertDocumentIsHardDeleted<LongDoc>(session, doc.Id);
        }

        [Fact]
        public void hard_delete_a_document_row_state_string()
        {
            using var session = theStore.OpenSession();
            var doc = new StringDoc{Id = Guid.NewGuid().ToString()};
            session.Store(doc);
            session.SaveChanges();

            session.HardDelete<StringDoc>(doc.Id);
            session.SaveChanges();

            assertDocumentIsHardDeleted<StringDoc>(session, doc.Id);
        }

        private static void userIsMarkedAsDeleted(IDocumentSession session, Guid userId)
        {
            var cmd = session.Connection.CreateCommand()
                .Sql("select mt_deleted, mt_deleted_at from softdelete.mt_doc_user where id = :id")
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

        [Fact]
        public void un_delete_a_document_by_where_row_state()
        {
            var user1 = new User { UserName = "foo" };
            var user2 = new User { UserName = "bar" };
            var user3 = new User { UserName = "baz" };

            using var session = theStore.OpenSession();
            session.Logger = new TestOutputMartenLogger(_output);

            session.Store(user1, user2, user3);
            session.SaveChanges();

            session.DeleteWhere<User>(x => x.UserName.StartsWith("b"));
            session.SaveChanges();

            userIsNotMarkedAsDeleted(session, user1.Id);
            userIsMarkedAsDeleted(session, user2.Id);
            userIsMarkedAsDeleted(session, user3.Id);

            session.UndoDeleteWhere<User>(x => x.UserName == "bar");
            session.SaveChanges();

            userIsNotMarkedAsDeleted(session, user1.Id);
            userIsNotMarkedAsDeleted(session, user2.Id);
            userIsMarkedAsDeleted(session, user3.Id);
        }

        #region sample_query_soft_deleted_docs
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

        #endregion sample_query_soft_deleted_docs

        #region sample_query_maybe_soft_deleted_docs
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

        #endregion sample_query_maybe_soft_deleted_docs

        #region sample_query_is_soft_deleted_docs
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

        #endregion sample_query_is_soft_deleted_docs

        #region sample_query_soft_deleted_since
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

                var epoch = session.MetadataFor(user3).DeletedAt;
                session.Delete(user4);
                session.SaveChanges();

                session.Query<User>().Where(x => x.DeletedSince(epoch.Value)).Select(x => x.UserName)
                    .ToList().ShouldHaveTheSameElementsAs("jack");
            }
        }

        #endregion sample_query_soft_deleted_since

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

                var epoch = session.MetadataFor(user4).DeletedAt;

                session.Query<User>().Where(x => x.DeletedBefore(epoch.Value)).Select(x => x.UserName)
                    .ToList().ShouldHaveTheSameElementsAs("baz");
            }
        }

        [Fact]
        public void top_level_of_hierarchy()
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
            theStore.Tenancy.Default.EnsureStorageExists(typeof(User));
            theStore.Tenancy.Default.EnsureStorageExists(typeof(File));

            using var session = theStore.OpenSession();

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

        [Fact]
        public async Task hard_delete_by_document_and_tenant_by_string()
        {
            var doc1 = new StringDoc{Id = "big"};

            theSession.ForTenant("read").Store(doc1);
            theSession.ForTenant("blue").Store(doc1);

            await theSession.SaveChangesAsync();

            theSession.ForTenant("red").HardDelete(doc1);

            using var query = theStore.QuerySession();

            var redCount = await query.Query<StringDoc>().Where(x => x.TenantIsOneOf("red"))
                .CountAsync();

            redCount.ShouldBe(0);

            var blueCount = await query.Query<StringDoc>().Where(x => x.TenantIsOneOf("blue"))
                .CountAsync();

            blueCount.ShouldBe(1);
        }

        [Fact]
        public async Task hard_delete_by_document_and_tenant_by_int()
        {
            var doc1 = new IntDoc{Id = 5000};

            theSession.ForTenant("red").Store(doc1);
            theSession.ForTenant("blue").Store(doc1);

            await theSession.SaveChangesAsync();

            theSession.ForTenant("red").HardDelete(doc1);

            using var query = theStore.QuerySession();

            var redCount = await query.Query<IntDoc>().Where(x => x.TenantIsOneOf("red"))
                .CountAsync();

            redCount.ShouldBe(0);

            var blueCount = await query.Query<IntDoc>().Where(x => x.TenantIsOneOf("blue"))
                .CountAsync();

            blueCount.ShouldBe(1);
        }

        [Fact]
        public async Task hard_delete_by_document_and_tenant_by_long()
        {
            var doc1 = new LongDoc{Id = 5000};

            theSession.ForTenant("red").Store(doc1);
            theSession.ForTenant("blue").Store(doc1);

            await theSession.SaveChangesAsync();

            theSession.ForTenant("red").HardDelete(doc1);

            using var query = theStore.QuerySession();

            var redCount = await query.Query<LongDoc>().Where(x => x.TenantIsOneOf("red"))
                .CountAsync();

            redCount.ShouldBe(0);

            var blueCount = await query.Query<LongDoc>().Where(x => x.TenantIsOneOf("blue"))
                .CountAsync();

            blueCount.ShouldBe(1);
        }

        [Fact]
        public async Task hard_delete_by_document_and_tenant_by_Guid()
        {
            var doc1 = new GuidDoc{Id = Guid.NewGuid()};


            theSession.ForTenant("red").Store(doc1);
            theSession.ForTenant("blue").Store(doc1);

            await theSession.SaveChangesAsync();

            theSession.ForTenant("red").HardDelete(doc1);

            using var query = theStore.QuerySession();

            var redCount = await query.Query<GuidDoc>().Where(x => x.TenantIsOneOf("red"))
                .CountAsync();

            redCount.ShouldBe(0);

            var blueCount = await query.Query<GuidDoc>().Where(x => x.TenantIsOneOf("blue"))
                .CountAsync();

            blueCount.ShouldBe(1);
        }
    }

    public class File
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; }
        public string Path { get; set; }
        public bool Deleted { get; set; }
        public DateTimeOffset? DeletedAt { get; set; }
    }

}
