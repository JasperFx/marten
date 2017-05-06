using Marten.Schema;

namespace Marten.Storage
{
    public abstract class Tenancy
    {
        public const string DefaultTenantId = "*DEFAULT*";

        protected Tenancy(StoreOptions options)
        {
            Options = options;
        }

        public StoreOptions Options { get; }

        protected void seedSchemas(ITenant tenant)
        {
            if (Options.AutoCreateSchemaObjects == AutoCreate.None) return;

            var allSchemaNames = Options.Storage.AllSchemaNames();
            var generator = new DatabaseSchemaGenerator(tenant);
            generator.Generate(Options, allSchemaNames);
        }
    }


    public class SingleTenant : Tenancy, ITenancy
    { 
        public SingleTenant(IConnectionFactory factory, StoreOptions options) : base(options)
        {
            Default = new Tenant(options.Storage, options, factory, Tenancy.DefaultTenantId);
        }

        public ITenant this[string tenantId] => Default;

        public ITenant Default { get; }

        public void Initialize()
        {
            seedSchemas(Default);
        }
    }
}