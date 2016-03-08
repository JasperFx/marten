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

            Exception<NpgsqlException>.ShouldBeThrownBy(() =>
            {
                using (var session = CreateSession())
                {
                    session.Delete(user);
                    session.SaveChanges();
                }
            });
        }
    }
}