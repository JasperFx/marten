using System;
using System.Collections.Generic;
using System.IO;
using Baseline;
using Marten.Schema;
using Marten.Services;
using Marten.Util;

namespace Marten.Events
{
    public class EventStoreAdmin : IEventStoreAdmin
    {
        private readonly IDocumentSchema _schema;
        private readonly IConnectionFactory _connectionFactory;
        private readonly StoreOptions _options;
        private readonly ISerializer _serializer;

        public TableName ModulesTable => new TableName(_options.Events.DatabaseSchemaName, "mt_modules");

        public FunctionName LoadProjectionBodyFunction => new FunctionName(_options.Events.DatabaseSchemaName, "mt_load_projection_body");
        public FunctionName InitializeProjectionsFunction => new FunctionName(_options.Events.DatabaseSchemaName, "mt_initialize_projections");
        public FunctionName GetProjectionUsageFunction => new FunctionName(_options.Events.DatabaseSchemaName, "mt_get_projection_usage");


        public EventStoreAdmin(IDocumentSchema schema, IConnectionFactory connectionFactory, StoreOptions options, ISerializer serializer)
        {
            _schema = schema;
            _connectionFactory = connectionFactory;
            _options = options;
            _serializer = serializer;
        }

        public void LoadProjections(string directory)
        {
            _schema.EnsureStorageExists(typeof(EventStream));

            var files = new FileSystem();

            using (var connection = new ManagedConnection(_connectionFactory))
            {
                files.FindFiles(directory, FileSet.Deep("*.js")).Each(file =>
                {
                    var body = files.ReadStringFromFile(file);
                    var name = Path.GetFileNameWithoutExtension(file);

                    connection.Execute(cmd =>
                    {
                        cmd.CallsSproc(LoadProjectionBodyFunction)
                            .With("proj_name", name)
                            .With("body", body)
                            .ExecuteNonQuery();

                    });
                });
            }
        }

        public void LoadProjection(string file)
        {
            throw new System.NotImplementedException();
        }

        public void ClearAllProjections()
        {
            throw new System.NotImplementedException();
        }

        public IEnumerable<ProjectionUsage> InitializeEventStoreInDatabase(bool overwrite = false)
        {
            _schema.EnsureStorageExists(typeof(EventStream));

            var js = SchemaBuilder.GetJavascript(_options, "mt_transforms");

            using (var connection = new ManagedConnection(_connectionFactory))
            {
                connection.Execute(cmd =>
                {
                    var sql = $"delete from {ModulesTable} where name = :name;" +
                              $"insert into {ModulesTable} (name, definition) values (:name, :definition)";

                    cmd.WithText(sql)
                        .With("name", "mt_transforms")
                        .With("definition", js)
                        .ExecuteNonQuery();
                });

                connection.Execute(cmd =>
                {
                    cmd.CallsSproc(InitializeProjectionsFunction)
                       .With("overwrite", overwrite).ExecuteNonQuery();
                });

            }

            return ProjectionUsages();
        }

        public IEnumerable<ProjectionUsage> ProjectionUsages()
        {
            string json = null;
            using (var connection = new ManagedConnection(_connectionFactory))
            {
                json = connection.Execute(cmd => cmd.CallsSproc(GetProjectionUsageFunction)
                    .ExecuteScalar().As<string>());
            }

            return _serializer.FromJson<ProjectionUsage[]>(json);
        }


        [Obsolete("This should be going away now that EventGraph puts things together itself")]
        public void RebuildEventStoreSchema()
        {
            runScript("mt_stream");
            runScript("mt_initialize_projections");
            runScript("mt_apply_transform");
            runScript("mt_apply_aggregation");
            

            var js = SchemaBuilder.GetJavascript(_options, "mt_transforms");

            using (var connection = new ManagedConnection(_connectionFactory))
            {
                connection.Execute(cmd =>
                {
                    cmd.WithText($"insert into {ModulesTable} (name, definition) values (:name, :definition)")
                        .With("name", "mt_transforms")
                        .With("definition", js)
                        .ExecuteNonQuery();
                });
            }
        }

        private void runScript(string script)
        {
            var sql = SchemaBuilder.GetSqlScript(_options.Events.DatabaseSchemaName, script);
            try
            {
                _connectionFactory.RunSql(sql);
            }
            catch (Exception e)
            {
                throw new MartenSchemaException(sql, e);
            }
        }

    }
}