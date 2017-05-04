using System;
using System.Collections.Concurrent;
using Marten.Schema;

namespace Marten.Storage
{
    public class Tenants
    {
        private readonly StoreOptions _options;
        public const string DefaultTenantId = "*DEFAULT*";
        private readonly ConcurrentDictionary<string, ITenant> _tenants = new ConcurrentDictionary<string, ITenant>();

        public Tenants(StoreOptions options)
        {
            _options = options;
        }

        internal void ApplyToAll(Action<ITenant> action)
        {
            
        }

        public ITenant Default => this[DefaultTenantId];

        public ITenant this[string tenantId]
        {
            get
            {
                return _tenants.GetOrAdd(tenantId, id =>
                {
                    var factory = _options.Tenancy.Create(tenantId, _options.AutoCreateSchemaObjects);

                    var tenant = new Tenant(_options.Storage, _options, factory, tenantId);
                    seedSchemas(tenant);

                    return tenant;
                });
            }
        }

        private void seedSchemas(ITenant tenant)
        {
            if (_options.AutoCreateSchemaObjects == AutoCreate.None) return;

            var allSchemaNames = _options.Storage.AllSchemaNames();
            var generator = new DatabaseSchemaGenerator(tenant);
            generator.Generate(_options, allSchemaNames);
        }
    }

    public interface ITenantStrategy
    {
        string[] AllKnownTenants();
        IConnectionFactory Create(string tenantId, AutoCreate autoCreate);
    }

    public class SingleTenant : ITenantStrategy
    {
        private readonly IConnectionFactory _factory;

        public SingleTenant(IConnectionFactory factory)
        {
            _factory = factory;
        }

        public string[] AllKnownTenants()
        {
            return new[] {Tenants.DefaultTenantId};
        }

        public IConnectionFactory Create(string tenantId, AutoCreate autoCreate)
        {
            return _factory;
        }
    }
}