using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Baseline;
using Marten.Internal;
using Marten.Internal.Storage;
using Marten.Schema;
using Marten.Schema.Identity.Sequences;
using Weasel.Core;
using Weasel.Core.Migrations;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;

namespace Marten.Storage
{
    public partial class MartenDatabase: PostgresqlDatabase, IMartenDatabase
    {
        private readonly StorageFeatures _features;


        private readonly StoreOptions _options;

        private Lazy<SequenceFactory> _sequences;

        public MartenDatabase(StoreOptions options, IConnectionFactory factory, string identifier)
            : base(options, options.AutoCreateSchemaObjects, options.Advanced.Migrator, identifier, factory.Create)
        {
            _features = options.Storage;
            _options = options;

            resetSequences();

            Providers = options.Providers;
        }

        public ISequences Sequences => _sequences.Value;

        public IProviderGraph Providers { get; }

        /// <summary>
        ///     Set the minimum sequence number for a Hilo sequence for a specific document type
        ///     to the specified floor. Useful for migrating data between databases
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="floor"></param>
        public Task ResetHiloSequenceFloor<T>(long floor)
        {
            var sequence = Sequences.SequenceFor(typeof(T));
            return sequence.SetFloor(floor);
        }

        public override void ResetSchemaExistenceChecks()
        {
            base.ResetSchemaExistenceChecks();
            resetSequences();
        }

        public async Task<IReadOnlyList<DbObjectName>> DocumentTables()
        {
            var tables = await SchemaTables().ConfigureAwait(false);
            return tables.Where(x => x.Name.StartsWith(SchemaConstants.TablePrefix)).ToList();
        }

        public async Task<IReadOnlyList<DbObjectName>> Functions()
        {
            await using var conn = CreateConnection();
            await conn.OpenAsync().ConfigureAwait(false);

            var schemaNames = AllSchemaNames();

            return await conn.ExistingFunctions("mt_%", schemaNames).ConfigureAwait(false);
        }

        public async Task<Table> ExistingTableFor(Type type)
        {
            var mapping = _features.MappingFor(type).As<DocumentMapping>();
            var expected = mapping.Schema.Table;

            await using var conn = CreateConnection();
            await conn.OpenAsync().ConfigureAwait(false);

            return await expected.FetchExisting(conn).ConfigureAwait(false);
        }


        public override IFeatureSchema[] BuildFeatureSchemas()
        {
            return _options.Storage.AllActiveFeatures(this).ToArray();
        }

        public override IFeatureSchema FindFeature(Type featureType)
        {
            return _features.FindFeature(featureType);
        }

        private void resetSequences()
        {
            _sequences = new Lazy<SequenceFactory>(() =>
            {
                var sequences = new SequenceFactory(_options, this);

                generateOrUpdateFeature(typeof(SequenceFactory), sequences, default).AsTask().GetAwaiter().GetResult();

                return sequences;
            });
        }
    }
}
