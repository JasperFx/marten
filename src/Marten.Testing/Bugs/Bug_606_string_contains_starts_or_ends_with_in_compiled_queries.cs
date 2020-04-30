using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Marten.Linq;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_606_string_contains_starts_or_ends_with_in_compiled_queries: IntegrationContext
    {
        [Fact]
        public void compiled_query_with_ends_with()
        {
            var query = theStore.Diagnostics.PreviewCommand(new WhereUsernameEndsWith("foo.com"));

            query.Parameters[0].Value.ShouldBe("%foo.com");
        }

        public class WhereUsernameEndsWith: ICompiledListQuery<User, User>
        {
            public string EndsWith { get; }

            public WhereUsernameEndsWith(string endsWith)
            {
                EndsWith = endsWith;
            }

            public Expression<Func<IQueryable<User>, IEnumerable<User>>> QueryIs()
            {
                return q => q.Where(u => u.UserName.EndsWith(EndsWith));
            }
        }

        [Fact]
        public void compiled_query_with_starts_with()
        {
            var query = theStore.Diagnostics.PreviewCommand(new WhereUsernameStartsWith("foo.com"));

            query.Parameters[0].Value.ShouldBe("foo.com%");
        }

        public class WhereUsernameStartsWith: ICompiledListQuery<User, User>
        {
            public string StartsWith { get; }

            public WhereUsernameStartsWith(string startsWith)
            {
                StartsWith = startsWith;
            }

            public Expression<Func<IQueryable<User>, IEnumerable<User>>> QueryIs()
            {
                return q => q.Where(u => u.UserName.StartsWith(StartsWith));
            }
        }

        [Fact]
        public void compiled_query_with_contains()
        {
            var query = theStore.Diagnostics.PreviewCommand(new WhereUsernameContains("foo.com"));

            query.Parameters[0].Value.ShouldBe("%foo.com%");
        }

        public class WhereUsernameContains: ICompiledListQuery<User, User>
        {
            public string Contains { get; }

            public WhereUsernameContains(string contains)
            {
                Contains = contains;
            }

            public Expression<Func<IQueryable<User>, IEnumerable<User>>> QueryIs()
            {
                return q => q.Where(u => u.UserName.Contains(Contains));
            }
        }

        public Bug_606_string_contains_starts_or_ends_with_in_compiled_queries(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
