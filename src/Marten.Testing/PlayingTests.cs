using System.Diagnostics;
using Npgsql;
using Shouldly;

namespace Marten.Testing
{
    public class PlayingTests
    {
        public void try_it_out()
        {

            Debug.WriteLine(ConnectionSource.ConnectionString);

            using (var connection = new NpgsqlConnection(ConnectionSource.ConnectionString))
            {
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = "select * from fake";

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        Debug.WriteLine(reader.GetString(0));
                    }
                }
            }
        }
    }
}