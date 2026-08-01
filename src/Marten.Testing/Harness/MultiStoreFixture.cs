using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Core.Reflection;
using Npgsql;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;

namespace Marten.Testing.Harness
{
    /// <summary>
    /// xUnit class or collection fixture holding a named registry of lazily built
    /// <see cref="DocumentStore"/> instances, one per <see cref="StoreOptions"/> "profile".
    /// Use this when the same test methods need to run against multiple store
    /// configurations (append mode, stream identity, tenancy style...).
    ///
    /// Theories should be parameterized by profile key, never by store instance —
    /// xUnit serializes theory data and never disposes MemberData-provided objects,
    /// so stores passed through theory data leak. Keys in the theory data, stores
    /// in the fixture.
    /// </summary>
    /// <remarks>
    /// Each profile gets its own schema named <c>{prefix}_{key}_{ProcessId}</c> so
    /// concurrent runs of different assemblies or TFMs against the shared database
    /// never collide. The schema is dropped before the store is first built and
    /// again when the fixture is disposed. Data written by one test is visible to
    /// the next test on the same profile — tests needing pristine state should
    /// clean explicitly or use unique stream ids.
    /// </remarks>
    public abstract class MultiStoreFixture: IAsyncLifetime
    {
        private readonly string _schemaPrefix;
        private readonly Dictionary<string, Action<StoreOptions>> _profiles = new();
        private readonly Dictionary<string, DocumentStore> _stores = new();
        private readonly object _lock = new();

        /// <param name="schemaPrefix">
        /// Short, lowercase, letter-leading prefix unique to this fixture type.
        /// Keep it terse: the full schema name {prefix}_{key}_{pid} must stay
        /// under PostgreSQL's 63-byte identifier limit with room for Marten's
        /// table name suffixes.
        /// </param>
        protected MultiStoreFixture(string schemaPrefix)
        {
            _schemaPrefix = schemaPrefix;
        }

        /// <summary>
        /// Register a store profile under the given key. Call from the subclass
        /// constructor. The store is not built until <see cref="StoreFor"/> is
        /// first called with this key.
        /// </summary>
        protected void Profile(string key, Action<StoreOptions> configure)
        {
            _profiles.Add(key, configure);
        }

        public IReadOnlyCollection<string> ProfileKeys => _profiles.Keys;

        public string SchemaFor(string key)
        {
            return $"{_schemaPrefix}_{key.ToLowerInvariant().Sanitize()}_{Environment.ProcessId}";
        }

        public DocumentStore StoreFor(string key)
        {
            lock (_lock)
            {
                if (_stores.TryGetValue(key, out var existing))
                {
                    return existing;
                }

                if (!_profiles.TryGetValue(key, out var configure))
                {
                    throw new ArgumentOutOfRangeException(nameof(key),
                        $"No store profile '{key}' is registered. Known profiles: {string.Join(", ", _profiles.Keys)}");
                }

                var schemaName = SchemaFor(key);
                dropSchema(schemaName);

                var store = DocumentStore.For(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.AutoCreateSchemaObjects = AutoCreate.All;
                    opts.DatabaseSchemaName = schemaName;

                    configure(opts);
                });

                _stores.Add(key, store);
                return store;
            }
        }

        private static void dropSchema(string schemaName)
        {
            using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
            conn.Open();
            conn.CreateCommand($"drop schema if exists {schemaName} cascade").ExecuteNonQuery();
        }

        public ValueTask InitializeAsync()
        {
            return default;
        }

        public ValueTask DisposeAsync()
        {
            foreach (var key in _stores.Keys.ToArray())
            {
                _stores[key].Dispose();
                dropSchema(SchemaFor(key));
            }

            _stores.Clear();

            return default;
        }
    }
}
