using Npgsql;

namespace Marten
{
    public interface IConnectionFactory
    {
        NpgsqlConnection Create();
    }
}