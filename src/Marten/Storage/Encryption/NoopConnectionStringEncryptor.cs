using System;
using Npgsql;

namespace Marten.Storage.Encryption;

/// <summary>
/// A no-operation implementation of IConnectionStringEncryptor that passes through connection strings without encryption.
/// This provides a consistent interface when no encryption is needed.
/// </summary>
internal class NoopConnectionStringEncryptor : IConnectionStringEncryptor
{
    /// <summary>
    /// Returns the connection string as-is without encryption
    /// </summary>
    public string Encrypt(string connectionString) => connectionString;

    /// <summary>
    /// Returns the connection string as-is without decryption
    /// </summary>
    public string Decrypt(string encryptedConnectionString) => encryptedConnectionString;

    /// <summary>
    /// Generates a parameterized SQL command for inserting or updating an unencrypted connection string
    /// </summary>
    public (string sql, object[] parameters) GetInsertSql(string schemaName, string tableName, string tenantId, string connectionString)
    {
        var sql = $"insert into {schemaName}.{tableName} (tenant_id, connection_string) values (?, ?) " +
                 "on conflict (tenant_id) do update set connection_string = ?";

        return (sql, [
            tenantId,
            connectionString,
            connectionString
        ]);
    }

    /// <summary>
    /// Generates a parameterized SQL command for selecting unencrypted connection strings
    /// </summary>
    public (string sql, object[] parameters) GetSelectSql(string schemaName, string tableName, string tenantId)
    {
        var whereClause = tenantId == string.Empty ? "" : " where (tenant_id = ?) ";
        var sql = $"select tenant_id, connection_string from {schemaName}.{tableName}{whereClause}";
        return (sql, tenantId == string.Empty ? [] : [tenantId]);
    }

}
