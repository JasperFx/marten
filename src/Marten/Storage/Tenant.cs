using System;
using System.Collections.Concurrent;
using System.IO;
using Baseline;
using Marten.Schema;
using Marten.Util;

namespace Marten.Storage
{
    public class Tenant
    {
        private readonly ConcurrentDictionary<Type, bool> _checks = new ConcurrentDictionary<Type, bool>();
        private readonly IConnectionFactory _factory;
        private readonly StorageFeatures _features;
        private readonly IMartenLogger _logger;
        private readonly StoreOptions _options;

        public Tenant(IMartenLogger logger, StorageFeatures features, StoreOptions options, IConnectionFactory factory,
            string tenantId)
        {
            TenantId = tenantId;
            _logger = logger;
            _features = features;
            _options = options;
            _factory = factory;
        }

        public string TenantId { get; }

        public void RemoveSchemaItems(Type featureType, StorageFeatures features)
        {
            var feature = features.FindFeature(featureType);
            var writer = new StringWriter();

            foreach (var schemaObject in feature.Objects)
                schemaObject.WriteDropStatement(_options.DdlRules, writer);

            _factory.RunSql(writer.ToString());
        }

        public void ResetSchemaExistenceChecks()
        {
            _checks.Clear();
        }

        public void EnsureStorageExists(Type featureType)
        {
            if (_checks.ContainsKey(featureType)) return;

            // TODO -- ensure the system type here too?
            var feature = _features.FindFeature(featureType);
            if (feature == null)
                throw new ArgumentOutOfRangeException(nameof(featureType),
                    $"Unknown feature type {featureType.FullName}");

            foreach (var dependentType in feature.DependentTypes())
                EnsureStorageExists(dependentType);


            using (var conn = _factory.Create())
            {
                conn.Open();

                var patch = new SchemaPatch(_options.DdlRules);
                patch.Apply(conn, _options.AutoCreateSchemaObjects, feature.Objects);

                var ddl = patch.UpdateDDL;
                if (patch.Difference != SchemaPatchDifference.None && ddl.IsNotEmpty())
                {
                    var cmd = conn.CreateCommand(ddl);
                    try
                    {
                        cmd.ExecuteNonQuery();
                        _options.Logger().SchemaChange(ddl);
                        _checks[featureType] = true;
                    }
                    catch (Exception e)
                    {
                        throw new MartenCommandException(cmd, e);
                    }
                }
            }
        }
    }
}