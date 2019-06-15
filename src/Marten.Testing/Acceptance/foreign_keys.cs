using System;
using Marten.Testing.Documents;
using Shouldly;
using Xunit;

namespace Marten.Testing.Acceptance
{
    public class foreign_keys: IntegratedFixture
    {
        [Fact]
        public void can_insert_document_with_null_value_of_foregin_key()
        {
            StoreOptions(options =>
            {
                options.Schema.For<Issue>().ForeignKey<User>(x => x.AssigneeId, fkd => fkd.CascadeDeletes = false);
            });
            theStore.Tenancy.Default.EnsureStorageExists(typeof(User));

            var issue = new Issue();

            ShouldProperlySave(issue);
        }

        [Fact]
        public void can_insert_document_with_existing_value_of_foregin_key()
        {
            StoreOptions(options =>
            {
                options.Schema.For<Issue>().ForeignKey<User>(x => x.AssigneeId, fkd => fkd.CascadeDeletes = false);
            });
            theStore.Tenancy.Default.EnsureStorageExists(typeof(User));

            var user = new User();
            using (var session = theStore.OpenSession())
            {
                session.Store(user);
                session.SaveChanges();
            }

            var issue = new Issue { AssigneeId = user.Id };

            ShouldProperlySave(issue);
        }

        [Fact]
        public void cannot_insert_document_with_non_existing_value_of_foregin_key()
        {
            StoreOptions(options =>
            {
                options.Schema.For<Issue>().ForeignKey<User>(x => x.AssigneeId, fkd => fkd.CascadeDeletes = false);
            });
            theStore.Tenancy.Default.EnsureStorageExists(typeof(User));

            var issue = new Issue { AssigneeId = Guid.NewGuid() };

            Should.Throw<MartenCommandException>(() =>
            {
                using (var session = theStore.OpenSession())
                {
                    session.Insert(issue);
                    session.SaveChanges();
                }
            });
        }

        [Fact]
        public void can_update_document_with_existing_value_of_foregin_key_to_other_existing_value()
        {
            StoreOptions(options =>
            {
                options.Schema.For<Issue>().ForeignKey<User>(x => x.AssigneeId, fkd => fkd.CascadeDeletes = false);
            });
            theStore.Tenancy.Default.EnsureStorageExists(typeof(User));

            var user = new User();
            var otherUser = new User();
            var issue = new Issue { AssigneeId = user.Id };

            using (var session = theStore.OpenSession())
            {
                session.Store(user, otherUser);
                session.Store(issue);
                session.SaveChanges();
            }

            issue.AssigneeId = otherUser.Id;

            ShouldProperlySave(issue);
        }

        [Fact]
        public void can_update_document_with_existing_value_of_foregin_key_to_null()
        {
            StoreOptions(options =>
            {
                options.Schema.For<Issue>().ForeignKey<User>(x => x.AssigneeId, fkd => fkd.CascadeDeletes = false);
            });
            theStore.Tenancy.Default.EnsureStorageExists(typeof(User));

            var user = new User();
            var otherUser = new User();
            var issue = new Issue { AssigneeId = user.Id };

            using (var session = theStore.OpenSession())
            {
                session.Store(user, otherUser);
                session.Store(issue);
                session.SaveChanges();
            }

            issue.AssigneeId = null;

            ShouldProperlySave(issue);
        }

        [Fact]
        public void cannot_update_document_with_existing_value_of_foregin_key_to_not_existing()
        {
            StoreOptions(options =>
            {
                options.Schema.For<Issue>().ForeignKey<User>(x => x.AssigneeId, fkd => fkd.CascadeDeletes = false);
            });
            theStore.Tenancy.Default.EnsureStorageExists(typeof(User));

            var user = new User();
            var otherUser = new User();
            var issue = new Issue { AssigneeId = user.Id };

            using (var session = theStore.OpenSession())
            {
                session.Store(user, otherUser);
                session.Store(issue);
                session.SaveChanges();
            }

            issue.AssigneeId = Guid.NewGuid();

            Should.Throw<MartenCommandException>(() =>
            {
                using (var session = theStore.OpenSession())
                {
                    session.Update(issue);
                    session.SaveChanges();
                }
            });
        }

        [Fact]
        public void can_delete_document_with_foreign_key()
        {
            StoreOptions(options =>
            {
                options.Schema.For<Issue>().ForeignKey<User>(x => x.AssigneeId, fkd => fkd.CascadeDeletes = true);
            });
            theStore.Tenancy.Default.EnsureStorageExists(typeof(User));

            var user = new User();
            var issue = new Issue { AssigneeId = user.Id };

            using (var session = theStore.OpenSession())
            {
                session.Store(user);
                session.Store(issue);
                session.SaveChanges();
            }

            using (var session = theStore.OpenSession())
            {
                session.Delete(issue);
                session.SaveChanges();
            }

            using (var query = theStore.QuerySession())
            {
                query.Load<Issue>(issue.Id).ShouldBeNull();
                query.Load<User>(user.Id).ShouldNotBeNull();
            }
        }

        [Fact]
        public void can_delete_document_that_is_referenced_by_foreignkey_with_cascadedeletes_from_other_document()
        {
            StoreOptions(options =>
            {
                options.Schema.For<Issue>().ForeignKey<User>(x => x.AssigneeId, fkd => fkd.CascadeDeletes = true);
            });
            theStore.Tenancy.Default.EnsureStorageExists(typeof(User));

            var user = new User();
            var issue = new Issue { AssigneeId = user.Id };

            using (var session = theStore.OpenSession())
            {
                session.Store(user);
                session.Store(issue);
                session.SaveChanges();
            }

            using (var session = theStore.OpenSession())
            {
                session.Delete(user);
                session.SaveChanges();
            }

            using (var query = theStore.QuerySession())
            {
                query.Load<Issue>(issue.Id).ShouldBeNull();
                query.Load<User>(user.Id).ShouldBeNull();
            }
        }

        [Fact]
        public void cannot_delete_document_that_is_referenced_by_foreignkey_without_cascadedeletes_from_other_document()
        {
            StoreOptions(options =>
            {
                options.Schema.For<Issue>().ForeignKey<User>(x => x.AssigneeId, fkd => fkd.CascadeDeletes = false);
            });
            theStore.Tenancy.Default.EnsureStorageExists(typeof(User));

            var user = new User();
            var issue = new Issue { AssigneeId = user.Id };

            using (var session = theStore.OpenSession())
            {
                session.Store(user);
                session.Store(issue);
                session.SaveChanges();
            }

            Should.Throw<MartenCommandException>(() =>
            {
                using (var session = theStore.OpenSession())
                {
                    session.Delete(user);
                    session.SaveChanges();
                }
            });

            using (var query = theStore.QuerySession())
            {
                query.Load<Issue>(issue.Id).ShouldNotBeNull();
                query.Load<User>(user.Id).ShouldNotBeNull();
            }
        }

        private void ShouldProperlySave(Issue issue)
        {
            using (var session = theStore.OpenSession())
            {
                session.Store(issue);
                session.SaveChanges();
            }

            using (var query = theStore.QuerySession())
            {
                var documentFromDb = query.Load<Issue>(issue.Id);

                documentFromDb.ShouldNotBeNull();
            }
        }
    }
}