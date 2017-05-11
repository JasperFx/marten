using Baseline;
using Marten.Schema;

namespace Marten.Storage
{
    public class SingleTenant : Tenancy, ITenancy
    {
        private readonly IConnectionFactory _factory;
        private readonly StoreOptions _options;

        public SingleTenant(IConnectionFactory factory, StoreOptions options) : base(options)
        {
            _factory = factory;
            _options = options;
            Default = new Tenant(options.Storage, options, factory, Tenancy.DefaultTenantId);
            Cleaner = new DocumentCleaner(options, Default);
            Schema = new TenantSchema(options, Default.As<Tenant>());
        }

        public ITenant this[string tenantId] => Default;

        public ITenant Default { get; }

        public void Initialize()
        {
            seedSchemas(Default);
        }

        public IDocumentCleaner Cleaner { get; }
        public IDocumentSchema Schema { get; }
        public TenancyStyle Style { get; } = TenancyStyle.Single;
    }
}