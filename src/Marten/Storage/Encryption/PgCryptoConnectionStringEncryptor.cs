using System;
using System.Threading;
using System.Threading.Tasks;
using Marten.Exceptions;
using Npgsql;

namespace Marten.Storage.Encryption;

/// <summary>
/// Provides database-level encryption and decryption for connection strings using PostgreSQL's pgcrypto extension.
/// Connection strings are encrypted and decrypted directly in the database using pgp_sym_encrypt and pgp_sym_decrypt functions.
/// </summary>
internal class PgCryptoConnectionStringEncryptor : IConnectionStringEncryptor
{
    private readonly string _encryptionKey;

    /// <summary>
    /// Initializes a new instance of the PgCrypto encryption provider.
    /// </summary>
    /// <param name="encryptionKey">The encryption key for pgcrypto functions</param>
    /// <exception cref="ArgumentNullException">Thrown when the encryption key is null</exception>
    public PgCryptoConnectionStringEncryptor(string encryptionKey)
    {
        _encryptionKey = encryptionKey ?? throw new ArgumentNullException(nameof(encryptionKey));
    }

    /// <summary>
    /// Returns the connection string as-is since encryption is handled at the database level.
    /// </summary>
    public string Encrypt(string connectionString) => connectionString;

    /// <summary>
    /// Returns the connection string as-is since decryption is handled at the database level.
    /// </summary>
    public string Decrypt(string encryptedConnectionString) => encryptedConnectionString;

    /// <summary>
    /// Generates SQL to insert or update an encrypted connection string using pgp_sym_encrypt.
    /// The encryption is performed by the database using the pgcrypto extension.
    /// </summary>
    public (string sql, object[] parameters) GetInsertSql(string schemaName, string tableName, string tenantId, string connectionString)
    {
        return ($"insert into {schemaName}.{tableName} (tenant_id, connection_string) " +
            $"values (?, pgp_sym_encrypt(?::text, ?::text)) " +
            $"on conflict (tenant_id) do update set connection_string = pgp_sym_encrypt(?::text, ?::text)",
            [
                tenantId,
                connectionString,
                _encryptionKey,
                connectionString,
                _encryptionKey,
            ]);
    }

    /// <summary>
    /// Generates SQL to select and decrypt connection strings using pgp_sym_decrypt.
    /// The decryption is performed by the database using the pgcrypto extension.
    /// </summary>
    public (string sql, object[] parameters) GetSelectSql(string schemaName, string tableName, string tenantId)
    {
        var whereClause = tenantId == "*" ? "" : " where tenant_id = ?";
        var sql = $"select tenant_id, pgp_sym_decrypt(connection_string::bytea, ?::text) as connection_string " +
                 $"from {schemaName}.{tableName}{whereClause}";

        return (sql, [_encryptionKey, tenantId]);
    }

    /// <summary>
    /// Ensures the pgcrypto extension is available and properly configured in the specified schema.
    /// </summary>
    public async Task EnsurePrerequisitesAsync(NpgsqlDataSource dataSource, string schemaName, CancellationToken token = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(token).ConfigureAwait(false);
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT EXISTS(" +
                "SELECT 1 FROM pg_extension e " +
                "WHERE e.extname = 'pgcrypto')";
            cmd.Parameters.AddWithValue("schema_name", schemaName);
            var extensionExists = (bool)await cmd.ExecuteScalarAsync(token).ConfigureAwait(false);

            if (!extensionExists)
            {
                throw new MartenException(
                    $"PgCrypto encryption requires the pgcrypto extension to be installed.\n" +
                    $"Run 'CREATE EXTENSION IF NOT EXISTS pgcrypto;' as a superuser or contact your database administrator.")
                {
                    HelpLink = "https://www.postgresql.org/docs/current/pgcrypto.html"
                };
            }
        }
        finally
        {
            await conn.CloseAsync().ConfigureAwait(false);
        }
    }
}
