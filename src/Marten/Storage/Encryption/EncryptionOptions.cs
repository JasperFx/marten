using System;

namespace Marten.Storage.Encryption;

/// <summary>
/// Specifies the encryption method to use for tenant database connection strings.
/// </summary>
public enum ConnectionStringEncryption
{
    /// <summary>
    /// No encryption of connection strings. Connection strings will be stored as plain text.
    /// </summary>
    None,

    /// <summary>
    /// Use AES encryption with a provided 32-byte encryption key.
    /// Connection strings are encrypted in memory using AES with a randomly generated IV.
    /// The encrypted data is stored as base64-encoded strings.
    /// </summary>
    AES,

    /// <summary>
    /// Use PostgreSQL's pgcrypto extension for encryption.
    /// Connection strings are encrypted and decrypted directly in the database using
    /// pgp_sym_encrypt and pgp_sym_decrypt functions. This requires the pgcrypto
    /// extension to be installed in the same schema as the tenant table.
    /// </summary>
    PgCrypto
}

/// <summary>
/// Options for configuring connection string encryption.
/// </summary>
public class EncryptionOptions
{
    private string? _encryptionKey;

    /// <summary>
    /// The type of encryption to use for connection strings.
    /// </summary>
    public ConnectionStringEncryption Type { get; private set; } = ConnectionStringEncryption.None;

    /// <summary>
    /// The encryption key used to encrypt/decrypt connection strings.
    /// Must be exactly 32 characters long.
    /// </summary>
    public string? Key
    {
        get => _encryptionKey;
        private set
        {
            if (value != null && string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Encryption key cannot be empty or whitespace", nameof(value));
            _encryptionKey = value;
        }
    }

    /// <summary>
    /// Use AES encryption with the specified key for connection strings.
    /// </summary>
    /// <param name="key">The encryption key for AES encryption</param>
    /// <returns>The current options instance for method chaining</returns>
    public EncryptionOptions UseAes(string key)
    {
        var keyBytes = Convert.FromBase64String(key);
        if (keyBytes.Length < 16 || keyBytes.Length > 32)
            throw new ArgumentException("AES encryption key must be between 16 and 32 bytes (128-256 bits) when base64 decoded", nameof(key));

        Key = key;
        Type = ConnectionStringEncryption.AES;
        return this;
    }

    /// <summary>
    /// Use PostgreSQL's pgcrypto extension with the specified key for connection strings.
    /// </summary>
    /// <param name="key">The encryption key for pgcrypto encryption</param>
    /// <returns>The current options instance for method chaining</returns>
    public EncryptionOptions UsePgCrypto(string key)
    {
        var keyBytes = Convert.FromBase64String(key);
        if (keyBytes.Length < 16)
            throw new ArgumentException("PgCrypto encryption key must be at least 16 bytes (128 bits) when base64 decoded", nameof(key));

        Key = key;
        Type = ConnectionStringEncryption.PgCrypto;
        return this;
    }

    /// <summary>
    /// Disable encryption for connection strings.
    /// </summary>
    /// <returns>The current options instance for method chaining</returns>
    public EncryptionOptions UseNoEncryption()
    {
        Key = null;
        Type = ConnectionStringEncryption.None;
        return this;
    }
}
