using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Marten.Exceptions;
using Marten.Linq;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class But_1561_compiled_query_returning_json : IntegrationContext
    {
        public But_1561_compiled_query_returning_json(DefaultStoreFixture fixture) : base(fixture)
        {
        }

        public class Customer
        {
            public Guid Id { get; set; }
            public string LastName { get; set; }
        }

        public class FindCustomerJsonByNameQuery : ICompiledQuery<Customer, string>
        {
            public string LastNamePrefix { get; set; } = string.Empty;

            public Expression<Func<IMartenQueryable<Customer>, string>> QueryIs()
            {
                return q => q
                    .Where(p => p.LastName.StartsWith(LastNamePrefix))
                    .OrderBy(p => p.LastName)
                    .ToJsonArray();
            }
        }

        public class FindCustomerJsonByNameQueryAsync : ICompiledQuery<Customer, Task<string>>
        {
            public string LastNamePrefix { get; set; } = string.Empty;

            public Expression<Func<IMartenQueryable<Customer>, Task<string>>> QueryIs()
            {
                return q => q
                    .Where(p => p.LastName.StartsWith(LastNamePrefix))
                    .OrderBy(p => p.LastName)
                    .ToJsonArrayAsync(CancellationToken.None);
            }
        }

        [Fact]
        public async Task friendly_message_about_async_operations_async()
        {
            var ex = await Should.ThrowAsync<InvalidCompiledQueryException>(async () =>
                await theSession.QueryAsync(new FindCustomerJsonByNameQueryAsync()));

            ex.Message.ShouldContain("Compiled queries cannot use asynchronous query selectors like 'CountAsync()'. Please use the synchronous equivalent like 'Count()' instead. You will still be able to query asynchronously through IQuerySession.QueryAsync().", StringComparisonOption.Default);
        }

        [Fact]
        public void friendly_message_about_async_operations_sync()
        {
            var ex = Should.Throw<InvalidCompiledQueryException>(() =>
                theSession.Query(new FindCustomerJsonByNameQueryAsync()));

            ex.Message.ShouldContain("Compiled queries cannot use asynchronous query selectors like 'CountAsync()'. Please use the synchronous equivalent like 'Count()' instead. You will still be able to query asynchronously through IQuerySession.QueryAsync().", StringComparisonOption.Default);
        }

        [Fact]
        public async Task can_query_that_way()
        {
            theStore.Advanced.Clean.DeleteAllDocuments();

            var customer1 = new Customer{LastName = "Sir Mixalot"};
            var customer2 = new Customer{LastName = "Sir Gawain"};
            var customer3 = new Customer{LastName = "Ser Jaime"};

            theSession.Store(customer1, customer2, customer3);
            await theSession.SaveChangesAsync();

            var customers = await theSession.QueryAsync(new FindCustomerJsonByNameQuery {LastNamePrefix = "Sir"});
            customers.Length.ShouldBe(148); // magic number that just happens to be the length of the JSON string returned
        }


    }
}
