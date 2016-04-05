using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Marten.Linq;
using Marten.Testing.Documents;
using Shouldly;
using StructureMap;
using Xunit;

namespace Marten.Testing
{
    public class CompileQueryDemonstrator
    {
        [Fact]
        public void single_item_compiled_query()
        {
            var container = Container.For<DevelopmentModeRegistry>();

            var store = container.GetInstance<IDocumentStore>();

            using (var session = store.QuerySession())
            {
                var user = session.Query(new UserByUsername {UserName = "myusername"});
                user.ShouldNotBeNull();
            }
        }

        [Fact]
        public void multiple_item_compiled_query()
        {
            var container = Container.For<DevelopmentModeRegistry>();

            var store = container.GetInstance<IDocumentStore>();

            using (var session = store.QuerySession())
            {
                var users = session.Query(new UsersByFirstName {FirstName = "Corey"}).ToList();
                users.ShouldNotBeEmpty();
            }
        }
    }

    public class UserByUsername : ISingleItemCompiledQuery<User, string>
    {
        public string UserName { get; set; }

        public Expression<Func<IQueryable<User>, string>> QueryIs()
        {
            return query => query.Where(x => x.UserName == UserName)
                .Select(x => x.UserName)
                .FirstOrDefault();
        }
    }

    public class UsersByFirstName : IMultipleItemCompiledQuery<User, User>
    {
        public string FirstName { get; set; }

        public Expression<Func<IQueryable<User>, IEnumerable<User>>> QueryIs()
        {
            return query => query.Where(x => x.FirstName == FirstName);
        }
    }
}