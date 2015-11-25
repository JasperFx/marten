using System;
using System.IO;
using FubuCore;
using Marten.Schema;
using Npgsql;
using StructureMap;

namespace Marten.Testing
{
    public class ConnectionSource : ConnectionFactory
    {
        public static readonly string ConnectionString;

        static ConnectionSource()
        {
            var path = AppDomain.CurrentDomain.BaseDirectory.AppendPath("connection.txt");
            if (!File.Exists(path))
            {
                path =
                    AppDomain.CurrentDomain.BaseDirectory.ParentDirectory()
                        .ParentDirectory()
                        .AppendPath("connection.txt");
            }


            ConnectionString = new FileSystem().ReadStringFromFile(path);
        }


        public ConnectionSource() : base(ConnectionString)
        {
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