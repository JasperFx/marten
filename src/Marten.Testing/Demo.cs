using System;
using System.Linq;
using System.Linq.Expressions;
using Marten.Linq;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing
{
    public class Demo
    {
        public class FindUser : ICompiledQuery<User>
        {
            public FindUser(string name)
            {
                Name = name;
            }

            public string Name { get; set; }

            public Expression<Func<IQueryable<User>, User>> QueryIs()
            {
                return x => x.FirstOrDefault(_ => _.UserName == Name);
            }
        }

        [Fact]
        public void try_stuff()
        {

            var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.Logger(new ConsoleMartenLogger());
                _.DatabaseSchemaName = "Demo";

                _.Schema.For<User>().Duplicate(x => x.UserName);
            });

            // Cleans out all the database artifacts
            store.Advanced.Clean.CompletelyRemoveAll();

            var user = new User {UserName = "ian"};

            using (var session = store.OpenSession())
            {
                session.Store(user);
                session.SaveChanges();

                var user2 = session.Query(new FindUser("ian"));
                SpecificationExtensions.ShouldNotBeNull(user2);
            }
        }
    }
}
