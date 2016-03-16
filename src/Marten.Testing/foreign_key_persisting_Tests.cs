using System;
using Marten.Schema;
using Marten.Services;
using Marten.Testing.Documents;
using Npgsql;
using Xunit;

namespace Marten.Testing
{
    public class foreign_key_persisting_Tests : DocumentSessionFixture<IdentityMap>
    {
        [Fact]
        public void persist_and_overwrite_foreign_key()
        {
            ((DocumentSchema) theStore.Schema).Alter(registry => registry.For<Issue>()
                .ForeignKey<User>(x => x.AssigneeId));

            var issue = new Issue();
            var user = new User();

            using (var session = CreateSession())
            {
                session.Store(user);
                session.Store(issue);
                session.SaveChanges();
            }

            issue.AssigneeId = user.Id;

            using (var session = CreateSession())
            {
                session.Store(issue);
                session.SaveChanges();
            }

            issue.AssigneeId = null;

            using (var session = CreateSession())
            {
                session.Store(issue);
                session.SaveChanges();
            }
        }

        [Fact]
        public void throws_exception_if_trying_to_delete_referenced_user()
        {
            ((DocumentSchema) theStore.Schema).Alter(registry => registry.For<Issue>()
                .ForeignKey<User>(x => x.AssigneeId));

            var issue = new Issue();
            var user = new User();

            issue.AssigneeId = user.Id;

            using (var session = CreateSession())
            {
                session.Store(user);
                session.Store(issue);
                session.SaveChanges();
            }

            Exception<NpgsqlException>.ShouldBeThrownBy(() =>
            {
                using (var session = CreateSession())
                {
                    session.Delete(user);
                    session.SaveChanges();
                }
            });
        }

        [Fact]
        public void persist_without_referenced_user()
        {
            ((DocumentSchema)theStore.Schema).Alter(registry => registry.For<Issue>()
               .ForeignKey<User>(x => x.AssigneeId));

            using (var session = CreateSession())
            {
                session.Store(new Issue());
                session.SaveChanges();
            }
        }

        [Fact]
        public void order_inserts()
        {
            ((DocumentSchema)theStore.Schema).Alter(registry => registry.For<Issue>()
               .ForeignKey<User>(x => x.AssigneeId));

            var issue = new Issue();
            var user = new User();

            issue.AssigneeId = user.Id;

            using (var session = CreateSession())
            {
                session.Store(issue);
                session.Store(user);

                session.SaveChanges();
            }
        }

        [Fact]
        public void throws_exception_on_cyclic_dependency()
        {
            ((DocumentSchema)theStore.Schema).Alter(registry =>
            {
                registry.For<Node1>().ForeignKey<Node3>(x => x.Link);
                registry.For<Node2>().ForeignKey<Node1>(x => x.Link);
                registry.For<Node3>().ForeignKey<Node2>(x => x.Link);
            });

            Exception<Exception>.ShouldBeThrownBy(() =>
            {
                using (var session = CreateSession())
                {
                    session.Store(new Node1());
                    session.Store(new Node2());
                    session.Store(new Node3());

                    session.SaveChanges();
                }
            });
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
    }
}