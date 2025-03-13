using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace Marten.Storage.Encryption;

/// <summary>
/// Provides encryption and decryption services for tenant database connection strings.
/// Implementations should handle both the encryption/decryption operations and
/// the generation of SQL commands for database operations involving encrypted data.
/// </summary>
internal interface IConnectionStringEncryptor
{
    /// <summary>
    /// Encrypts a connection string using the provider's encryption method.
    /// </summary>
    /// <param name="connectionString">The connection string to encrypt</param>
    /// <returns>The encrypted connection string</returns>
    string Encrypt(string connectionString);

    /// <summary>
    /// Decrypts an encrypted connection string using the provider's decryption method.
    /// If decryption fails, implementations should return the original string.
    /// </summary>
    /// <param name="encryptedConnectionString">The encrypted connection string to decrypt</param>
    /// <returns>The decrypted connection string, or the original string if decryption fails</returns>
    string Decrypt(string encryptedConnectionString);

    /// <summary>
    /// Generates a parameterized SQL command for inserting or updating an encrypted connection string.
    /// </summary>
    /// <param name="schemaName">The database schema name</param>
    /// <param name="tableName">The table name</param>
    /// <param name="tenantId">The tenant identifier</param>
    /// <param name="connectionString">The connection string to encrypt and store</param>
    /// <returns>A tuple containing the SQL command text and its parameters</returns>
    (string sql, object[] parameters) GetInsertSql(string schemaName, string tableName, string tenantId, string connectionString);

    /// <summary>
    /// Generates a parameterized SQL command for selecting and decrypting connection strings.
    /// </summary>
    /// <param name="schemaName">The database schema name</param>
    /// <param name="tableName">The table name</param>
    /// <param name="tenantId">The tenant identifier, use "*" to select all tenants</param>
    /// <returns>A tuple containing the SQL command text and its parameters</returns>
    (string sql, object[] parameters) GetSelectSql(string schemaName, string tableName, string tenantId);

    /// <summary>
    /// Ensures any prerequisites required by the encryption provider are met.
    /// For example, checking if required database extensions are installed.
    /// </summary>
    /// <param name="dataSource">The database data source to check against</param>
    /// <param name="schemaName">The database schema name</param>
    /// <param name="token">Optional cancellation token</param>
    /// <returns>A task representing the asynchronous operation</returns>
    Task EnsurePrerequisitesAsync(NpgsqlDataSource dataSource, string schemaName, CancellationToken token = default)
    {
        // Default implementation assumes no prerequisites are needed
        return Task.CompletedTask;
    }
}
