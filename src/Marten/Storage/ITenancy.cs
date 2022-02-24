using System;
using System.Collections.Generic;
using System.Linq;
using Baseline.ImTools;
using Marten.Schema;
using Npgsql;
using Weasel.Core.Migrations;

namespace Marten.Storage
{
    public interface ITenancy : IDatabaseSource
    {
        Tenant GetTenant(string tenantId);
        Tenant Default { get; }
        IDocumentCleaner Cleaner { get; }

    }

    public class UnknownTenantIdException: Exception
    {
        public UnknownTenantIdException(string tenantId) : base($"Unknown tenant id '{tenantId}'")
        {
        }
    }

    public class StaticMultiTenancy: Tenancy, ITenancy
    {
        private ImHashMap<string, Tenant> _tenants = ImHashMap<string, Tenant>.Empty;
        private ImHashMap<string, MartenDatabase> _databases = ImHashMap<string, MartenDatabase>.Empty;

        public StaticMultiTenancy(StoreOptions options) : base(options)
        {
            Cleaner = new CompositeDocumentCleaner(this);
        }

        public DatabaseExpression AddDatabase(string connectionString, string databaseIdentifier = null)
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            var identifier = databaseIdentifier ?? builder.Database;

            var database = new MartenDatabase(Options, new ConnectionFactory(connectionString), identifier);
            _databases = _databases.AddOrUpdate(identifier, database);

            return new DatabaseExpression(this, database);
        }

        public void AddDatabaseForSingleTenant(string connectionString, string tenantId)
        {
            var database = new MartenDatabase(Options, new ConnectionFactory(connectionString), tenantId);
            _databases = _databases.AddOrUpdate(tenantId, database);

            var expression = new DatabaseExpression(this, database).ForTenants(tenantId);

            if (Default == null)
            {
                expression.AsDefault();
            }
        }

        public class DatabaseExpression
        {
            private readonly StaticMultiTenancy _parent;
            private readonly MartenDatabase _database;

            internal DatabaseExpression(StaticMultiTenancy parent, MartenDatabase database)
            {
                _parent = parent;
                _database = database;
            }

            /// <summary>
            /// Tells Marten that the designated tenant ids are stored in the current database
            /// </summary>
            /// <param name="tenantIds"></param>
            /// <returns></returns>
            public DatabaseExpression ForTenants(params string[] tenantIds)
            {
                foreach (var tenantId in tenantIds)
                {
                    var tenant = new Tenant(tenantId, _database);
                    _parent._tenants = _parent._tenants.AddOrUpdate(tenantId, tenant);
                }

                return this;
            }

            public DatabaseExpression AsDefault()
            {
                _parent.Default = new Tenant(DefaultTenantId, _database);
                return this;
            }
        }

        public IReadOnlyList<IDatabase> BuildDatabases()
        {
            return _databases.Enumerate().Select(x => x.Value).ToList();
        }

        public Tenant GetTenant(string tenantId)
        {
            if (_tenants.TryFind(tenantId, out var tenant))
            {
                return tenant;
            }

            throw new UnknownTenantIdException(tenantId);
        }

        public Tenant Default { get; private set; }
        public IDocumentCleaner Cleaner { get; }
    }
}
