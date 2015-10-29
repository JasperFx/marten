using System;
using System.IO;
using FubuCore;
using Marten.Generation;
using Marten.Schema;
using Marten.Testing.Documents;
using Marten.Testing.Fixtures;
using Npgsql;
using StructureMap;

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
            using (var container = Container.For<DevelopmentModeRegistry>())
            {
                var cleaner = container.GetInstance<DocumentCleaner>();

                cleaner.CompletelyRemoveAll();
            }


        }
    }
}