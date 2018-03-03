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
            // SAMPLE: configuring-initial-data
            var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);

                // Add as many implementations of IInitialData as you need
                _.InitialData.Add(new InitialData(InitialDatasets.Companies));
                _.InitialData.Add(new InitialData(InitialDatasets.Users));
            });
            // ENDSAMPLE

            using (var session = store.QuerySession())
            {
                foreach (var initialUser in InitialDatasets.Users)
                {
                    var user = session.Load<User>(initialUser.Id);
                    user.FirstName.ShouldBe(initialUser.FirstName);
                    user.LastName.ShouldBe(initialUser.LastName);
                }

                foreach (var initialCompany in InitialDatasets.Companies)
                {
                    var company = session.Load<Company>(initialCompany.Id);
                    company.Name.ShouldBe(initialCompany.Name);
                }
            }

            store.Dispose();
        }
    }

    // SAMPLE: initial-data
    public class InitialData : IInitialData
    {
        private readonly object[] _initialData;

        public InitialData(params object[] initialData)
        {
            _initialData = initialData;
        }

        public void Populate(IDocumentStore store)
        {
            using (var session = store.LightweightSession())
            {
                // Marten UPSERT will cater for existing records
                session.Store(_initialData);
                session.SaveChanges();
            }
        }
    }
    
    public static class InitialDatasets
    {
        public static readonly Company[] Companies =
        {
            new Company { Name = "Netram Ltd." },
            new Company { Name = "Acme Inc." }
        };

        public static readonly User[] Users =
        {
            new User { FirstName = "Danger" , LastName = "Mouse" },
            new User { FirstName = "Speedy" , LastName = "Gonzales" }
        };
    }
    // ENDSAMPLE
}
