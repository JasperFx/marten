using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace Marten.Storage.Encryption;

/// <summary>
/// Provides AES-based encryption and decryption for connection strings using a 32-byte key.
/// Connection strings are encrypted in memory using AES with a randomly generated IV.
/// </summary>
internal class AesConnectionStringEncryptor : IConnectionStringEncryptor
{
    private readonly string _encryptionKey;

    /// <summary>
    /// Initializes a new instance of the AES encryption provider.
    /// </summary>
    /// <param name="encryptionKey">The encryption key for AES encryption</param>
    /// <exception cref="ArgumentException">Thrown when the key is null or empty</exception>
    public AesConnectionStringEncryptor(string encryptionKey)
    {
        if (string.IsNullOrWhiteSpace(encryptionKey))
            throw new ArgumentException("AES encryption key cannot be empty or whitespace", nameof(encryptionKey));

        _encryptionKey = encryptionKey;
    }

    public string Encrypt(string connectionString)
    {
        using var aes = Aes.Create();
        using var deriveBytes = new Rfc2898DeriveBytes(_encryptionKey, 16, 1000, HashAlgorithmName.SHA256);
        aes.Key = deriveBytes.GetBytes(32);
        aes.IV = deriveBytes.GetBytes(16);

        using var encryptor = aes.CreateEncryptor();
        var plainTextBytes = Encoding.UTF8.GetBytes(connectionString);
        var cipherTextBytes = encryptor.TransformFinalBlock(plainTextBytes, 0, plainTextBytes.Length);

        var result = new byte[aes.IV.Length + cipherTextBytes.Length];
        Buffer.BlockCopy(deriveBytes.Salt, 0, result, 0, 16);
        Buffer.BlockCopy(cipherTextBytes, 0, result, 16, cipherTextBytes.Length);

        return Convert.ToBase64String(result);
    }

    public string Decrypt(string encryptedConnectionString)
    {
        try
        {
            var cipherTextBytes = Convert.FromBase64String(encryptedConnectionString);
            if (cipherTextBytes.Length < 16) // IV size
                return encryptedConnectionString;

            using var aes = Aes.Create();
            var salt = new byte[16];
            var cipher = new byte[cipherTextBytes.Length - 16];
            Buffer.BlockCopy(cipherTextBytes, 0, salt, 0, 16);
            Buffer.BlockCopy(cipherTextBytes, 16, cipher, 0, cipherTextBytes.Length - 16);
            using var deriveBytes = new Rfc2898DeriveBytes(_encryptionKey, salt, 1000, HashAlgorithmName.SHA256);
            aes.Key = deriveBytes.GetBytes(32);
            aes.IV = deriveBytes.GetBytes(16);

            using var decryptor = aes.CreateDecryptor();
            var plainTextBytes = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
            return Encoding.UTF8.GetString(plainTextBytes);
        }
        catch
        {
            // If decryption fails, return the original string
            return encryptedConnectionString;
        }
    }

    public (string sql, object[] parameters) GetInsertSql(string schemaName, string tableName, string tenantId, string connectionString)
    {
        var encryptedString = Encrypt(connectionString);
        return ($"insert into {schemaName}.{tableName} (tenant_id, connection_string) values (?, ?) " +
            "on conflict (tenant_id) do update set connection_string = ?",
        [
            tenantId,
            encryptedString,
            encryptedString
        ]);
    }

    public (string sql, object[] parameters) GetSelectSql(string schemaName, string tableName, string tenantId)
    {
        var whereClause = tenantId == string.Empty ? "" : " where (tenant_id = ?) ";
        return ($"select tenant_id, connection_string from {schemaName}.{tableName}{whereClause}",
            tenantId == string.Empty ? [] : [tenantId]);
    }

    // No prerequisites needed for AES encryption since it's done in memory
}
