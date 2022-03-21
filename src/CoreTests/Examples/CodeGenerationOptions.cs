using System.Threading.Tasks;
using Baseline;
using LamarCodeGeneration;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace CoreTests.Examples
{
    public class CodeGenerationOptions
    {
        public void build_store()
        {
            #region sample_code_generation_modes

            using var store = DocumentStore.For(opts =>
            {
                opts.Connection("some connection string");

                // This is the default. Marten will always generate
                // code dynamically at runtime
                opts.GeneratedCodeMode = TypeLoadMode.Dynamic;

                // Marten will only use types that are compiled into
                // the application assembly ahead of time. This is the
                // V4 "pre-built" model
                opts.GeneratedCodeMode = TypeLoadMode.Static;

                // New for V5. More explanation in the docs:)
                opts.GeneratedCodeMode = TypeLoadMode.Auto;
            });

            #endregion
        }

        public async Task using_auto()
        {
            #region sample_document_store_for_user_document

            using var store = DocumentStore.For(opts =>
            {
                // ConnectionSource is a little helper in the Marten
                // test suite
                opts.Connection(ConnectionSource.ConnectionString);

                opts.GeneratedCodeMode = TypeLoadMode.Auto;
            });

            #endregion

            #region sample_save_a_single_user

            await using var session = store.LightweightSession();
            var user = new User { UserName = "admin" };
            session.Store(user);
            await session.SaveChangesAsync();

            #endregion
        }

        [Fact]
        public void override_application_assembly()
        {
            #region sample_using_set_application_project

            using var host = Host.CreateDefaultBuilder()
                .ConfigureServices(services =>
                {
                    services.AddMarten("some connection string");

                    services.SetApplicationProject(typeof(User).Assembly);
                })
                .StartAsync();

            #endregion
        }
    }
}
