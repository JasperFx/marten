using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Marten.Linq;
using Marten.Schema;
using Marten.Testing.Harness;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class UserBug1886
    {
        public Guid Id { get; set; }

        [DuplicateField]
        public string NormalizedUserName { get; set; }

        [DuplicateField]
        public string NormalizedEmail { get; set; }
    }

    public class FindUserBug1886ByNameQuery : ICompiledQuery<UserBug1886, UserBug1886>
    {
        public string NormalizedName { get; set; } = string.Empty;

        public Expression<Func<IMartenQueryable<UserBug1886>, UserBug1886>> QueryIs()
        {
            return q => q.FirstOrDefault(
                x => x.NormalizedUserName == NormalizedName
                     || x.NormalizedEmail == NormalizedName);
        }
    }

    public class Bug_1886_compiled_query_value_retention: BugIntegrationContext
    {
        [Fact]
        public async Task Should_be_able_to_run_query_multiple_times()
        {
            var insertSession = theStore.LightweightSession();
            await using (insertSession)
            {
                var testUser = new UserBug1886 { NormalizedEmail = "TEST@EXAMPLE.COM", NormalizedUserName = "ADMIN" };
                insertSession.Insert(testUser);
                await insertSession.SaveChangesAsync();
            }

            var session1 = theStore.QuerySession();
            await using (session1)
            {
                var foundUser = await session1
                    .QueryAsync(
                        new FindUserBug1886ByNameQuery
                        {
                            NormalizedName = "ADMIN",
                        });
                Assert.NotNull(foundUser);
            }

            var session2 = theStore.QuerySession();
            await using (session2)
            {
                var foundUser = await session2
                    .QueryAsync(
                        new FindUserBug1886ByNameQuery
                        {
                            NormalizedName = "TEST@EXAMPLE.COM",
                        });
                Assert.NotNull(foundUser);
            }
        }
    }
}
