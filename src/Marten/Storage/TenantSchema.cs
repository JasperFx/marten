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
        private readonly Tenant _tenant;

        public TenantSchema(StoreOptions options, Tenant tenant)
        {
            _features = options.Storage;
            _tenant = tenant;
            StoreOptions = options;
            DdlRules = options.DdlRules;
        }

        public StoreOptions StoreOptions { get; }

        public DdlRules DdlRules { get; }

        public void WriteDDL(string filename, bool transactionalScript = true)
        {
            var sql = ToDDL(transactionalScript);
            new FileSystem().WriteStringToFile(filename, sql);
        }

        public void WriteDDLByType(string directory, bool transactionalScript=true)
        {
            var system = new FileSystem();

            system.DeleteDirectory(directory);
            system.CreateDirectory(directory);

            var features = _features.AllActiveFeatures(_tenant).ToArray();
            writeDatabaseSchemaGenerationScript(directory, system, features);


            foreach (var feature in features)
            {
                var writer = new StringWriter();
                feature.Write(StoreOptions.DdlRules, writer);

                var file = directory.AppendPath(feature.Identifier + ".sql");

                new SchemaPatch(StoreOptions.DdlRules).WriteFile(file, writer.ToString(), transactionalScript);
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

        public string ToDDL(bool transactionalScript = true)
        {
            var writer = new StringWriter();

            new SchemaPatch(StoreOptions.DdlRules).WriteScript(writer, w =>
            {
                var allSchemaNames = StoreOptions.Storage.AllSchemaNames();
                DatabaseSchemaGenerator.WriteSql(StoreOptions, allSchemaNames, w);

                foreach (var feature in _features.AllActiveFeatures(_tenant))
                {
                    feature.Write(StoreOptions.DdlRules, writer);
                }
            }, transactionalScript);

            return writer.ToString();
        }


        public void WritePatch(string filename, bool withSchemas = true, bool transactionalScript = true)
        {
            if (!Path.IsPathRooted(filename))
            {
                filename = AppContext.BaseDirectory.AppendPath(filename);
            }

            var patch = ToPatch(withSchemas, withAutoCreateAll: true);

            patch.WriteUpdateFile(filename, transactionalScript);

            var dropFile = SchemaPatch.ToDropFileName(filename);
            patch.WriteRollbackFile(dropFile, transactionalScript);
        }

        public SchemaPatch ToPatch(bool withSchemas = true, bool withAutoCreateAll = false)
        {
            var patch = new SchemaPatch(StoreOptions.DdlRules);

            if (withSchemas)
            {
                var allSchemaNames = StoreOptions.Storage.AllSchemaNames();
                DatabaseSchemaGenerator.WriteSql(StoreOptions, allSchemaNames, patch.UpWriter);
            }

            var @objects = _features.AllActiveFeatures(_tenant).SelectMany(x => x.Objects).ToArray();

            using (var conn = _tenant.CreateConnection())
            {
                conn.Open();

                patch.Apply(conn, withAutoCreateAll ? AutoCreate.All : StoreOptions.AutoCreateSchemaObjects, @objects);
            }

            return patch;
        }

        public void AssertDatabaseMatchesConfiguration()
        {
            var patch = ToPatch(false, withAutoCreateAll:true);

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
                    _tenant.RunSql(ddl);
                    StoreOptions.Logger().SchemaChange(ddl);

                    _tenant.MarkAllFeaturesAsChecked();
                }
                catch (Exception e)
                {
                    throw new MartenSchemaException("All Configured Changes", ddl, e);
                }
            }
        }

        public SchemaPatch ToPatch(Type documentType)
        {
            var mapping = _features.MappingFor(documentType);

            var patch = new SchemaPatch(StoreOptions.DdlRules);

            using (var conn = _tenant.CreateConnection())
            {
                conn.Open();

                patch.Apply(conn, AutoCreate.CreateOrUpdate, mapping.As<IFeatureSchema>().Objects);
            }

            return patch;
        }

        public void WritePatchByType(string directory, bool transactionalScript = true)
        {
            var system = new FileSystem();

            system.DeleteDirectory(directory);
            system.CreateDirectory(directory);

            var features = _features.AllActiveFeatures(_tenant).ToArray();
            writeDatabaseSchemaGenerationScript(directory, system, features);

            using (var conn = _tenant.CreateConnection())
            {
                conn.Open();

                foreach (var feature in features)
                {
                    var patch = new SchemaPatch(StoreOptions.DdlRules);
                    patch.Apply(conn, AutoCreate.CreateOrUpdate, feature.Objects);

                    if (patch.UpdateDDL.IsNotEmpty())
                    {
                        var file = directory.AppendPath(feature.Identifier + ".sql");
                        patch.WriteUpdateFile(file, transactionalScript);
                    }
                }

            }



        }
    }
}