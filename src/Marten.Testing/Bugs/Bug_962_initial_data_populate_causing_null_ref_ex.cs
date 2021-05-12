using System;
using System.Threading.Tasks;
using Marten.Schema;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_962_initial_data_populate_causing_null_ref_ex
    {
        [Fact]
        public void initial_data_should_populate_db()
        {
            #region sample_configuring-initial-data
            var store = DocumentStore.For(_ =>
            {
                _.DatabaseSchemaName = "Bug962";

                _.Connection(ConnectionSource.ConnectionString);

                // Add as many implementations of IInitialData as you need
                _.InitialData.Add(new InitialData(InitialDatasets.Companies));
                _.InitialData.Add(new InitialData(InitialDatasets.Users));
            });
            #endregion sample_configuring-initial-data

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

    #region sample_initial-data
    public class InitialData: IInitialData
    {
        private readonly object[] _initialData;

        public InitialData(params object[] initialData)
        {
            _initialData = initialData;
        }

        public async Task Populate(IDocumentStore store)
        {
            using var session = store.LightweightSession();
            // Marten UPSERT will cater for existing records
            session.Store(_initialData);
            await session.SaveChangesAsync();
        }
    }

    public static class InitialDatasets
    {
        public static readonly Company[] Companies =
        {
            new Company { Id = Guid.Parse("2219b6f7-7883-4629-95d5-1a8a6c74b244"), Name = "Netram Ltd." },
            new Company { Id = Guid.Parse("642a3e95-5875-498e-8ca0-93639ddfebcd"), Name = "Acme Inc." }
        };

        public static readonly User[] Users =
        {
            new User { Id = Guid.Parse("331c15b4-b7bd-44d6-a804-b6879f99a65f"),FirstName = "Danger" , LastName = "Mouse" },
            new User { Id = Guid.Parse("9d8ef25a-de9a-41e5-b72b-13f24b735883"), FirstName = "Speedy" , LastName = "Gonzales" }
        };
    }

    #endregion sample_initial-data
}
