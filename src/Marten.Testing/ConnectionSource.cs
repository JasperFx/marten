using System;
using System.IO;
using FubuCore;
using Marten.Generation;
using Marten.Testing.Documents;
using Marten.Testing.Fixtures;
using Npgsql;

namespace Marten.Testing
{
    public class ConnectionSource : IConnectionFactory
    {
        private readonly static Lazy<string> _connectionString = new Lazy<string>(() =>
        {
            var path = AppDomain.CurrentDomain.BaseDirectory.AppendPath("connection.txt");
            if (!File.Exists(path))
            {
                path = AppDomain.CurrentDomain.BaseDirectory.ParentDirectory().ParentDirectory().AppendPath("connection.txt");
            }


            return new FileSystem().ReadStringFromFile(path);
        }); 

        public static string ConnectionString
        {
            get { return _connectionString.Value; }
        }

        public NpgsqlConnection Create()
        {
            return new NpgsqlConnection(ConnectionString);
        }

        public static void CleanBasicDocuments()
        {
            using (var runner = new CommandRunner(ConnectionString))
            {
                runner.Execute("DROP TABLE IF EXISTS {0} CASCADE;".ToFormat(SchemaBuilder.TableNameFor(typeof (User))));
                runner.Execute("DROP TABLE IF EXISTS {0} CASCADE;".ToFormat(SchemaBuilder.TableNameFor(typeof (Issue))));
                runner.Execute("DROP TABLE IF EXISTS {0} CASCADE;".ToFormat(SchemaBuilder.TableNameFor(typeof (Company))));
                runner.Execute("DROP TABLE IF EXISTS {0} CASCADE;".ToFormat(SchemaBuilder.TableNameFor(typeof (Target))));

                runner.Execute("DROP FUNCTION if exists {0}(docId UUID, doc JSON)".ToFormat(SchemaBuilder.UpsertNameFor(typeof(User))));
                runner.Execute("DROP FUNCTION if exists {0}(docId UUID, doc JSON)".ToFormat(SchemaBuilder.UpsertNameFor(typeof(Issue))));
                runner.Execute("DROP FUNCTION if exists {0}(docId UUID, doc JSON)".ToFormat(SchemaBuilder.UpsertNameFor(typeof(Company))));
                runner.Execute("DROP FUNCTION if exists {0}(docId UUID, doc JSON)".ToFormat(SchemaBuilder.UpsertNameFor(typeof(Target))));
            }
        }
    }
}