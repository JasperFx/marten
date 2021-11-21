using System;
using System.Collections.Generic;
using System.Linq;
using Marten.Testing.Harness;
using Xunit;

namespace Marten.Testing.Linq
{
    public class NgramSearchTests : IntegrationContext
    {
        public NgramSearchTests(DefaultStoreFixture fixture) : base(fixture)
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
        public void test_ngram_search_returns_data()
        {
            string term = null;
            var userDictionary = new Dictionary<int, User>();
            using var session = theStore.OpenSession();
            for (var i = 1; i < 4; i++)
            {
                var guid = $"{Guid.NewGuid():N}";
                if (term == null)
                {
                    term = guid.Substring(5);
                }

                var newUser = new User(i, $"Test user {guid}");

                session.Store(newUser);
            }

            session.SaveChanges();

            var query = session.Query<User>()
                //.Where(x => x.Content.PlainTextSearch(term)).ToList();
                //.Where(x => x.Content.Search(term)).ToList();
                .Where(x => x.UserName.NgramSearch(term)).ToList();
            
            query.ShouldNotBeNull();
            query.First().UserName.ShouldContain(term);
        }
    }
}
