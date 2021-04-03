using Npgsql;
#nullable enable
namespace Marten
{
    /// <summary>
    ///     Factory interface to customize the construction of an NpgsqlConnection
    ///     to the Postgresql database
    /// </summary>
    public interface IConnectionFactory
    {
        /// <summary>
        ///     Create a new, isolated connection to the Postgresql database
        /// </summary>
        /// <returns></returns>
        NpgsqlConnection Create();
    }
}
