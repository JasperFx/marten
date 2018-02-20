using Marten.Schema;
using Marten.Testing.Documents;
using Shouldly;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_962_initial_data_populate_causing_null_ref_ex
    {
        [Fact]
        public void initial_data_should_populate_db()
        {
            var seedData = new User[]
            {
                new User { FirstName = "Danger" , LastName = "Mouse" },
                new User { FirstName = "Speedy" , LastName = "Gonzales" }
            };

            using (var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.InitialData.Add(new UserSeedData(seedData));
            }))
            {
                using (var session = store.QuerySession())
                {
                    foreach (var seed in seedData)
                    {
                        var user = session.Load<User>(seed.Id);
                        user.FirstName.ShouldBe(seed.FirstName);
                        user.LastName.ShouldBe(seed.LastName);
                    }
                }
            }
        }
    }

    public class UserSeedData : IInitialData
    {
        private readonly User[] _seedData;

        public UserSeedData(params User[] seedData)
        {
            _seedData = seedData;
        }

        public void Populate(IDocumentStore store)
        {
            using (var session = store.OpenSession())
            {
                session.Store(_seedData);
                session.SaveChanges();
            }
        }
    }
}
