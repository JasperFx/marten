using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Baseline;
using Marten.Exceptions;
using Marten.Schema;
using Weasel.Postgresql;

namespace Marten.Storage
{
    internal class TenantSchema: IDocumentSchema
    {
        private readonly StorageFeatures _features;
        private readonly Tenant _tenant;

        public TenantSchema(StoreOptions options, Tenant tenant)
        {
            _features = options.Storage;
            _tenant = tenant;
            StoreOptions = options;
            DdlRules = options.Advanced.DdlRules;
        }

        public StoreOptions StoreOptions { get; }

        public DdlRules DdlRules { get; }

        public void WriteDatabaseCreationScriptFile(string filename)
        {
            var sql = ToDatabaseScript();
            new FileSystem().WriteStringToFile(filename, sql);
        }

        public void WriteDatabaseCreationScriptByType(string directory)
        {
            var system = new FileSystem();

            system.DeleteDirectory(directory);
            system.CreateDirectory(directory);

            var features = _features.AllActiveFeatures(_tenant).ToArray();
            writeDatabaseSchemaGenerationScript(directory, system, features);

            foreach (var feature in features)
            {
                var file = directory.AppendPath(feature.Identifier + ".sql");

                DdlRules.WriteTemplatedFile(file, (r, w) =>
                {
                    feature.Write(r, w);
                });
            }
        }

        private void writeDatabaseSchemaGenerationScript(string directory, FileSystem system, IFeatureSchema[] schemaObjects)
        {
            var allSchemaNames = StoreOptions.Storage.AllSchemaNames();
            var script = DatabaseSchemaGenerator.GenerateScript(StoreOptions, allSchemaNames);

            var writer = new StringWriter();

            if (script.IsNotEmpty())
            {
                writer.WriteLine(script);

                writer.WriteLine();
            }

            foreach (var feature in schemaObjects)
            {
                writer.WriteLine($"\\i {feature.Identifier}.sql");
            }

            var filename = directory.AppendPath("all.sql");
            system.WriteStringToFile(filename, writer.ToString());
        }

        public async Task<SchemaMigration> CreateMigrationAsync()
        {
            var @objects = _features.AllActiveFeatures(_tenant).SelectMany(x => x.Objects).ToArray();

            using var conn = _tenant.CreateConnection();
            await conn.OpenAsync().ConfigureAwait(false);

            return await SchemaMigration.Determine(conn, @objects).ConfigureAwait(false);
        }

        public string ToDatabaseScript()
        {
            var writer = new StringWriter();

            StoreOptions.Advanced.DdlRules.WriteScript(writer, (r, w) =>
            {
                var allSchemaNames = StoreOptions.Storage.AllSchemaNames();
                DatabaseSchemaGenerator.WriteSql(StoreOptions, allSchemaNames, w);

                foreach (var feature in _features.AllActiveFeatures(_tenant))
                {
                    feature.Write(r, w);
                }
            });

            return writer.ToString();
        }

        public async Task WriteMigrationFileAsync(string filename)
        {
            if (!Path.IsPathRooted(filename))
            {
                filename = AppContext.BaseDirectory.AppendPath(filename);
            }

            var patch = await CreateMigrationAsync().ConfigureAwait(false);

            DdlRules.WriteTemplatedFile(filename, (r, w) =>
            {
                patch.WriteAllUpdates(w, r, AutoCreate.All);
            });

            var dropFile = SchemaMigration.ToDropFileName(filename);
            DdlRules.WriteTemplatedFile(dropFile, (r, w) =>
            {
                patch.WriteAllRollbacks(w, r);
            });
        }

        public async Task AssertDatabaseMatchesConfigurationAsync()
        {
            var patch = await CreateMigrationAsync().ConfigureAwait(false);
            if (patch.Difference != SchemaPatchDifference.None)
            {
                throw new SchemaValidationException(patch.UpdateSql);
            }
        }

        public async Task ApplyAllConfiguredChangesToDatabaseAsync(AutoCreate? withCreateSchemaObjects = null)
        {
            var defaultAutoCreate = StoreOptions.AutoCreateSchemaObjects != AutoCreate.None
                ? StoreOptions.AutoCreateSchemaObjects
                : AutoCreate.CreateOrUpdate;

            var patch = await CreateMigrationAsync().ConfigureAwait(false);

            if (patch.Difference == SchemaPatchDifference.None) return;

            using var conn = _tenant.CreateConnection();
            await conn.OpenAsync().ConfigureAwait(false);

            try
            {
                var martenLogger = StoreOptions.Logger();
                await patch.ApplyAll(conn, DdlRules, withCreateSchemaObjects ?? defaultAutoCreate, sql => martenLogger.SchemaChange(sql)).ConfigureAwait(false);

                _tenant.MarkAllFeaturesAsChecked();
            }
            catch (Exception e)
            {
                throw new MartenSchemaException("All Configured Changes", patch.UpdateSql, e);
            }
        }

        public async Task<SchemaMigration> CreateMigrationAsync(Type documentType)
        {
            var mapping = _features.MappingFor(documentType);

            using var conn = _tenant.CreateConnection();
            await conn.OpenAsync().ConfigureAwait(false);

            var migration = await SchemaMigration.Determine(conn, mapping.Schema.Objects).ConfigureAwait(false);

            return migration;
        }

        public async Task WriteMigrationFileByTypeAsync(string directory)
        {
            var system = new FileSystem();

            system.DeleteDirectory(directory);
            system.CreateDirectory(directory);
            var features = _features.AllActiveFeatures(_tenant).ToArray();
            writeDatabaseSchemaGenerationScript(directory, system, features);

            using var conn = _tenant.CreateConnection();
            await conn.OpenAsync().ConfigureAwait(false);

            foreach (var feature in features)
            {
                var migration = await SchemaMigration.Determine(conn, feature.Objects).ConfigureAwait(false);

                if (migration.Difference == SchemaPatchDifference.None)
                {
                    continue;
                }

                var file = directory.AppendPath(feature.Identifier + ".sql");
                DdlRules.WriteTemplatedFile(file, (r, w) =>
                {
                    migration.WriteAllUpdates(w, r, AutoCreate.CreateOrUpdate);
                });
            }
        }
    }
}
