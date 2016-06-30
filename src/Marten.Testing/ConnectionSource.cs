using System;
using System.IO;
using Baseline;
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
            string path = null;
#if NET46
            path = AppDomain.CurrentDomain.BaseDirectory.AppendPath("connection.txt");
            if (!File.Exists(path))
            {
                path =
                    AppDomain.CurrentDomain.BaseDirectory.ParentDirectory()
                        .ParentDirectory()
                        .AppendPath("connection.txt");
            }

#else
            var ps = Microsoft.Extensions.PlatformAbstractions.PlatformServices.Default;
            path = ps.Application.ApplicationBasePath.AppendPath("connection.txt");
#endif
            ConnectionString = new FileSystem().ReadStringFromFile(path);
        }


        public ConnectionSource() : base(ConnectionString)
        {
        }

    }
}