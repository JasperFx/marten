using System;
using System.IO;
using System.Linq;
using Baseline;
using Marten.Schema;

namespace Marten.Storage
{
    public class TenantSchema : IDocumentSchema
    {
        private readonly StorageFeatures _features;
        private readonly IConnectionFactory _factory;
        private readonly Tenant _tenant;

        public TenantSchema(StoreOptions options, IConnectionFactory factory, Tenant tenant)
        {
            _features = options.Storage;
            _factory = factory;
            _tenant = tenant;
            StoreOptions = options;
        }

        public StoreOptions StoreOptions { get; }

        public void WriteDDL(string filename)
        {
            var sql = ToDDL();
            new FileSystem().WriteStringToFile(filename, sql);
        }

        public void WriteDDLByType(string directory)
        {
            var system = new FileSystem();

            system.DeleteDirectory(directory);
            system.CreateDirectory(directory);

            var features = _features.AllActiveFeatures().ToArray();
            writeDatabaseSchemaGenerationScript(directory, system, features);


            foreach (var feature in features)
            {
                var writer = new StringWriter();
                feature.Write(StoreOptions.DdlRules, writer);

                var file = directory.AppendPath(feature.Identifier + ".sql");

                new SchemaPatch(StoreOptions.DdlRules).WriteTransactionalFile(file, writer.ToString());
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

        public string ToDDL()
        {
            var writer = new StringWriter();

            new SchemaPatch(StoreOptions.DdlRules).WriteTransactionalScript(writer, w =>
            {
                var allSchemaNames = StoreOptions.Storage.AllSchemaNames();
                DatabaseSchemaGenerator.WriteSql(StoreOptions, allSchemaNames, w);

                foreach (var feature in _features.AllActiveFeatures())
                {
                    feature.Write(StoreOptions.DdlRules, writer);
                }
            });

            return writer.ToString();
        }

        public IDbObjects DbObjects => new DbObjects(_factory, _features);

        public void WritePatch(string filename, bool withSchemas = true)
        {
            if (!Path.IsPathRooted(filename))
            {
                filename = AppContext.BaseDirectory.AppendPath(filename);
            }

            var patch = ToPatch(withSchemas);

            patch.WriteUpdateFile(filename);

            var dropFile = SchemaPatch.ToDropFileName(filename);
            patch.WriteRollbackFile(dropFile);
        }

        public SchemaPatch ToPatch(bool withSchemas = true)
        {
            var patch = new SchemaPatch(StoreOptions.DdlRules);

            if (withSchemas)
            {
                var allSchemaNames = StoreOptions.Storage.AllSchemaNames();
                DatabaseSchemaGenerator.WriteSql(StoreOptions, allSchemaNames, patch.UpWriter);
            }

            var @objects = _features.AllActiveFeatures().SelectMany(x => x.Objects).ToArray();

            using (var conn = _factory.Create())
            {
                conn.Open();

                patch.Apply(conn, StoreOptions.AutoCreateSchemaObjects, @objects);
            }

            return patch;
        }

        public void AssertDatabaseMatchesConfiguration()
        {
            var patch = ToPatch(false);

            if (patch.UpdateDDL.Trim().IsNotEmpty())
            {
                throw new SchemaValidationException(patch.UpdateDDL);
            }
        }

        public void ApplyAllConfiguredChangesToDatabase()
        {
            var patch = ToPatch(true);
            var ddl = patch.UpdateDDL.Trim();
            if (ddl.IsNotEmpty())
            {
                try
                {
                    _factory.RunSql(ddl);
                    StoreOptions.Logger().SchemaChange(ddl);

                    _tenant.MarkAllFeaturesAsChecked();
                }
                catch (Exception e)
                {
                    throw new MartenSchemaException("All Configured Changes", ddl, e);
                }
            }
        }

        public void EnsureFunctionExists(string functionName)
        {
            _tenant.EnsureStorageExists(typeof(SystemFunctions));
        }

        public SchemaPatch ToPatch(Type documentType)
        {
            var mapping = _features.MappingFor(documentType);

            var patch = new SchemaPatch(StoreOptions.DdlRules);

            using (var conn = _factory.Create())
            {
                conn.Open();

                patch.Apply(conn, AutoCreate.CreateOrUpdate, mapping.As<IFeatureSchema>().Objects);
            }

            return patch;
        }

        public void WritePatchByType(string directory)
        {
            var system = new FileSystem();

            system.DeleteDirectory(directory);
            system.CreateDirectory(directory);

            var features = _features.AllActiveFeatures().ToArray();
            writeDatabaseSchemaGenerationScript(directory, system, features);

            using (var conn = _factory.Create())
            {
                conn.Open();

                foreach (var feature in features)
                {
                    var patch = new SchemaPatch(StoreOptions.DdlRules);
                    patch.Apply(conn, AutoCreate.CreateOrUpdate, feature.Objects);

                    if (patch.UpdateDDL.IsNotEmpty())
                    {
                        var file = directory.AppendPath(feature.Identifier + ".sql");
                        patch.WriteUpdateFile(file);
                    }
                }

            }



        }
    }
}