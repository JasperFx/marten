using System;
using FubuCore;

namespace Marten.Testing
{
    public static class ConnectionSource
    {
        private readonly static Lazy<string> _connectionString = new Lazy<string>(() =>
        {
            var path =
                AppDomain.CurrentDomain.BaseDirectory.ParentDirectory().ParentDirectory().AppendPath("connection.txt");

            return new FileSystem().ReadStringFromFile(path);
        }); 

        public static string ConnectionString
        {
            get { return _connectionString.Value; }
        }
    }
}