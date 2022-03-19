using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Indexes
{
    public class NgramSearchTests : Marten.Testing.Harness.IntegrationContext
    {
        public NgramSearchTests(Marten.Testing.Harness.DefaultStoreFixture fixture) : base(fixture)
        {
        }

        public sealed class User
        {
            public int Id { get; set; }
            public string UserName { get; set; }

            public User(int id, string userName)
            {
                Id = id;
                UserName = userName;
            }
        }

        [Fact]
        public async Task test_ngram_search_returns_data()
        {
            string term = null;

            await using var session = theStore.OpenSession();

            for (var i = 1; i < 4; i++)
            {
                var guid = $"{Guid.NewGuid():N}";
                term ??= guid.Substring(5);

                var newUser = new User(i, $"Test user {guid}");

                session.Store(newUser);
            }

            await session.SaveChangesAsync();

            #region sample_ngram_search
            var result = await session
                .Query<User>()
                .Where(x => x.UserName.NgramSearch(term))
                .ToListAsync();
            #endregion

            result.ShouldNotBeNull();
            result.ShouldHaveSingleItem();
            result[0].UserName.ShouldContain(term);
        }
    }
}
