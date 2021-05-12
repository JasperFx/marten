using Marten.Schema;
using Weasel.Postgresql;

namespace Marten.Storage
{
    internal abstract class Tenancy
    {
        public const string DefaultTenantId = "*DEFAULT*";

        protected Tenancy(StoreOptions options)
        {
            Options = options;
        }

        internal StoreOptions Options { get; }

        protected void seedSchemas(ITenant tenant)
        {
            if (Options.AutoCreateSchemaObjects == AutoCreate.None)
                return;

            var allSchemaNames = Options.Storage.AllSchemaNames();
            var generator = new DatabaseSchemaGenerator(tenant);
            generator.Generate(Options, allSchemaNames);
        }
    }
}
