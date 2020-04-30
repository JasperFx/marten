using System;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Acceptance
{
    public class foreign_keys: IntegrationContext
    {
        [Fact]
        public void can_insert_document_with_null_value_of_foreign_key()
        {
            ConfigureForeignKeyWithCascadingDeletes(false);

            var issue = new Issue();

            ShouldProperlySave(issue);
        }

        [Fact]
        public void can_insert_document_with_existing_value_of_foreign_key()
        {
            ConfigureForeignKeyWithCascadingDeletes(false);

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
        public void cannot_insert_document_with_non_existing_value_of_foreign_key()
        {
            ConfigureForeignKeyWithCascadingDeletes(false);

            var issue = new Issue { AssigneeId = Guid.NewGuid() };

            Should.Throw<Marten.Exceptions.MartenCommandException>(() =>
            {
                using (var session = theStore.OpenSession())
                {
                    session.Insert(issue);
                    session.SaveChanges();
                }
            });
        }

        [Fact]
        public void can_update_document_with_existing_value_of_foreign_key_to_other_existing_value()
        {
            ConfigureForeignKeyWithCascadingDeletes(false);

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
        public void can_update_document_with_existing_value_of_foreign_key_to_null()
        {
            ConfigureForeignKeyWithCascadingDeletes(false);

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
        public void cannot_update_document_with_existing_value_of_foreign_key_to_not_existing()
        {
            ConfigureForeignKeyWithCascadingDeletes(false);

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

            Should.Throw<Marten.Exceptions.MartenCommandException>(() =>
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
            ConfigureForeignKeyWithCascadingDeletes(true);

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
                SpecificationExtensions.ShouldBeNull(query.Load<Issue>(issue.Id));
                SpecificationExtensions.ShouldNotBeNull(query.Load<User>(user.Id));
            }
        }

        [Fact]
        public void can_delete_document_that_is_referenced_by_foreignkey_with_cascadedeletes_from_other_document()
        {
            ConfigureForeignKeyWithCascadingDeletes(true);

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
                SpecificationExtensions.ShouldBeNull(query.Load<Issue>(issue.Id));
                SpecificationExtensions.ShouldBeNull(query.Load<User>(user.Id));
            }
        }

        [Fact]
        public void cannot_delete_document_that_is_referenced_by_foreignkey_without_cascadedeletes_from_other_document()
        {
            ConfigureForeignKeyWithCascadingDeletes(false);

            var user = new User();
            var issue = new Issue { AssigneeId = user.Id };

            using (var session = theStore.OpenSession())
            {
                session.Store(user);
                session.Store(issue);
                session.SaveChanges();
            }

            Should.Throw<Marten.Exceptions.MartenCommandException>(() =>
            {
                using (var session = theStore.OpenSession())
                {
                    session.Delete(user);
                    session.SaveChanges();
                }
            });

            using (var query = theStore.QuerySession())
            {
                SpecificationExtensions.ShouldNotBeNull(query.Load<Issue>(issue.Id));
                SpecificationExtensions.ShouldNotBeNull(query.Load<User>(user.Id));
            }
        }

        private void ConfigureForeignKeyWithCascadingDeletes(bool hasCascadeDeletes)
        {
            StoreOptions(options =>
            {
                options.Schema.For<Issue>().ForeignKey<User>(x => x.AssigneeId, fkd => fkd.CascadeDeletes = hasCascadeDeletes);
            });
            theStore.Tenancy.Default.EnsureStorageExists(typeof(User));
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

                SpecificationExtensions.ShouldNotBeNull(documentFromDb);
            }
        }

        public foreign_keys(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
