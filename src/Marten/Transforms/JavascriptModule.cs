using System;
using System.IO;
using System.Linq.Expressions;
using Baseline;
using Marten.Schema;
using Marten.Services;
using Marten.Util;
using Npgsql;

namespace Marten.Transforms
{
    public class JavascriptModule
    {
        public JavascriptModule(string id, string javascript)
        {
            Id = id;
            Javascript = javascript;
        }

        public string Id { get; set; }
        public string Javascript { get; set; }

        public static JavascriptModule FromFile(string file, string name = null)
        {
            var js = new FileSystem().ReadStringFromFile(file);

            return new JavascriptModule(name ?? Path.GetFileNameWithoutExtension(file), js);
        }
    }

    public interface ITransforms
    {
        void LoadTransform(string file, string name = null);
        void LoadFromDirectory(string directory);

        void Transform<T>(Action<Transformation<T>> configure);

    }

    public class Transforms : ITransforms
    {
        private readonly IConnectionFactory _factory;
        private readonly StoreOptions _options;
        private readonly IDocumentSchema _schema;
        private readonly TableName _table;

        public Transforms(StoreOptions options, IDocumentSchema schema)
        {
            _factory = options.ConnectionFactory();
            _options = options;
            _schema = schema;

            _table = new TableName(_options.DatabaseSchemaName, "mt_transforms");
        }

        public void LoadTransform(string file, string name = null)
        {

            var js = new FileSystem().ReadStringFromFile(file);


            using (var conn = new ManagedConnection(_factory, CommandRunnerMode.AutoCommit))
            {
                if (!_schema.DbObjects.TableExists(_table))
                {
                    var sql = SchemaBuilder.GetSqlScript(_table.Schema, "mt_transforms");
                    try
                    {
                        conn.Execute(cmd => cmd.Sql(sql).ExecuteNonQuery());
                        _options.Logger().SchemaChange(sql);
                    }
                    catch (Exception ex)
                    {
                        throw new MartenSchemaException(sql, ex);
                    }
                }

                conn.Execute(
                    cmd =>
                    {
                        cmd.CommandText = 
                            $"delete from {_table.QualifiedName} where name = :name;insert into {_table.QualifiedName} (name, definition) values (:name, :definition)";

                        cmd.AddParameter("name", name ?? Path.GetFileNameWithoutExtension(file));
                        cmd.AddParameter("definition", js);

                        cmd.ExecuteNonQuery();
                    });

            }
        }

        public void LoadFromDirectory(string directory)
        {
            var files = new FileSystem().FindFiles(directory, FileSet.Deep("*.js"));

            files.Each(file => LoadTransform(file));
        }

        public void Transform<T>(Action<Transformation<T>> configure)
        {
            var transformation = new Transformation<T>();
            configure(transformation);

            var command = ToSql(transformation);

            using (var conn = new ManagedConnection(_factory, CommandRunnerMode.AutoCommit))
            {
                conn.Execute(command, c => c.ExecuteNonQuery());
            }
        }


        public NpgsqlCommand ToSql<T>(Transformation<T> transformation)
        {
            throw new NotImplementedException();
        }
    }

    public class Transformation<T>
    {
        public Expression<Func<T, bool>> Where { get; set; }

        public string Name { get; set; }

        public string File { get; set; }

        public string Code { get; set; }

        public object Id { get; set; }

        

    }
}