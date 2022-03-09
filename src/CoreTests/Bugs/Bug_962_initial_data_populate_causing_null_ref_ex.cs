using System;
using System.Threading;
using System.Threading.Tasks;
using Marten;
using Marten.Schema;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace CoreTests.Bugs
{
    public class Bug_962_initial_data_populate_causing_null_ref_ex
    {
        [Fact]
        public async Task initial_data_should_populate_db()
        {
            #region sample_configuring-initial-data

            using var host = await Host.CreateDefaultBuilder()
                .ConfigureServices(services =>
                {
                    services.AddMarten(opts =>
                    {
                        opts.DatabaseSchemaName = "Bug962";

                        opts.Connection(ConnectionSource.ConnectionString);
                    })
                        // Add as many implementations of IInitialData as you need
                        .InitializeWith(new InitialData(InitialDatasets.Companies), new InitialData(InitialDatasets.Users));
                }).StartAsync();

            var store = host.Services.GetRequiredService<IDocumentStore>();
            #endregion

            await using var session = store.QuerySession();
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
    }

    #region sample_initial-data
    public class InitialData: IInitialData
    {
        private readonly object[] _initialData;

        public InitialData(params object[] initialData)
        {
            _initialData = initialData;
        }

        public async Task Populate(IDocumentStore store, CancellationToken cancellation)
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

    #endregion
}
