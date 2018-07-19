using System;

namespace Marten.Testing
{
    public class ConnectionSource : ConnectionFactory
    {
        public static readonly string ConnectionString = Environment.GetEnvironmentVariable("marten_testing_database")
            ?? "Host=localhost;Port=5432;Database=postgres;Username=postgres;password=postgres";

        static ConnectionSource()
        {
            if (ConnectionString.IsEmpty())
                throw new Exception(
                    "You need to set the connection string for your local Postgresql database in the environment variable 'marten_testing_database'");
        }


        public ConnectionSource() : base(ConnectionString)
        {
        }
    }
}