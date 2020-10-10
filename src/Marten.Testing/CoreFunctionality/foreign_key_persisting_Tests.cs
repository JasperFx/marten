using System;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Xunit;

namespace Marten.Testing.CoreFunctionality
{
    public class foreign_key_persisting_Tests: IntegrationContext
    {
        [Fact]
        public void persist_and_overwrite_foreign_key()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<Issue>().ForeignKey<User>(x => x.AssigneeId);
            });

            var issue = new Issue();
            var user = new User();

            using (var session = theStore.OpenSession())
            {
                session.Store(user);
                session.Store(issue);
                session.SaveChanges();
            }

            issue.AssigneeId = user.Id;

            using (var session = theStore.OpenSession())
            {
                session.Store(issue);
                session.SaveChanges();
            }

            issue.AssigneeId = null;

            using (var session = theStore.OpenSession())
            {
                session.Store(issue);
                session.SaveChanges();
            }
        }

        [Fact]
        public void throws_exception_if_trying_to_delete_referenced_user()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<Issue>()
                    .ForeignKey<User>(x => x.AssigneeId);
            });

            var issue = new Issue();
            var user = new User();

            issue.AssigneeId = user.Id;

            using (var session = theStore.OpenSession())
            {
                session.Store(user);
                session.Store(issue);
                session.SaveChanges();
            }

            Exception<Marten.Exceptions.MartenCommandException>.ShouldBeThrownBy(() =>
            {
                using (var session = theStore.OpenSession())
                {
                    session.Delete(user);
                    session.SaveChanges();
                }
            });
        }

        [Fact]
        public void persist_without_referenced_user()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<Issue>()
                    .ForeignKey<User>(x => x.AssigneeId);
            });

            using (var session = theStore.OpenSession())
            {
                session.Store(new Issue());
                session.SaveChanges();
            }
        }

        [Fact]
        public void order_inserts()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<Issue>()
                    .ForeignKey<User>(x => x.AssigneeId);
            });

            var issue = new Issue();
            var user = new User();

            issue.AssigneeId = user.Id;

            using (var session = theStore.OpenSession())
            {
                session.Store(issue);
                session.Store(user);

                session.SaveChanges();
            }
        }

        [Fact]
        public void throws_exception_on_cyclic_dependency()
        {
            Exception<InvalidOperationException>.ShouldBeThrownBy(() =>
            {
                StoreOptions(_ =>
                {
                    _.Schema.For<Node1>().ForeignKey<Node3>(x => x.Link);
                    _.Schema.For<Node2>().ForeignKey<Node1>(x => x.Link);
                    _.Schema.For<Node3>().ForeignKey<Node2>(x => x.Link);
                });
            }).Message.ShouldContain("Cyclic");

        }

        public class Node1
        {
            public Guid Id { get; set; }
            public Guid Link { get; set; }
        }

        public class Node2
        {
            public Guid Id { get; set; }
            public Guid Link { get; set; }
        }

        public class Node3
        {
            public Guid Id { get; set; }
            public Guid Link { get; set; }
        }

        public foreign_key_persisting_Tests(DefaultStoreFixture fixture) : base(fixture)
        {
            DocumentTracking = DocumentTracking.IdentityOnly;
        }
    }
}
