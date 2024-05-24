#nullable enable
using System;
using Npgsql;

namespace Marten;

/// <summary>
///     Factory interface to customize the construction of an NpgsqlConnection
///     to the Postgresql database
/// </summary>
[Obsolete("This will be removed in Marten 8, please prefer NpgsqlDataSource usage")]
public interface IConnectionFactory
{
    /// <summary>
    ///     Create a new, isolated connection to the Postgresql database
    /// </summary>
    /// <returns></returns>
    NpgsqlConnection Create();
}
