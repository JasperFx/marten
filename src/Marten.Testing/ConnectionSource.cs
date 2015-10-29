using System;
using System.IO;
using FubuCore;
using Marten.Generation;
using Marten.Schema;
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
            var cleaner = new DocumentCleaner(new ConnectionSource());
            cleaner.CompletelyRemove(typeof(User));
            cleaner.CompletelyRemove(typeof(Issue));
            cleaner.CompletelyRemove(typeof(Company));
            cleaner.CompletelyRemove(typeof(Target));

        }
    }
}