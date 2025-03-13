using System;
using System.Security.Cryptography;

namespace Marten.Storage.Encryption;

/// <summary>
/// Utility class for generating secure encryption keys compatible with Marten's encryption providers.
/// </summary>
public static class KeyGenerator
{
    /// <summary>
    /// Generates a secure random encryption key suitable for use with Marten's encryption providers.
    /// The key is generated using a cryptographically secure random number generator.
    /// </summary>
    /// <param name="byteLength">The number of random bytes to generate. Default is 24 bytes which produces a 32-character base64 string.</param>
    /// <param name="allowAnyByteLength">Use this flag for only testing purposes.</param>
    /// <returns>A base64-encoded string suitable for use as an encryption key</returns>
    public static string GenerateKey(int byteLength = 24, bool allowAnyByteLength = false)
    {
        if (byteLength < 16 && !allowAnyByteLength)
            throw new ArgumentException("For security, key should be at least 16 bytes (128 bits)", nameof(byteLength));

        var keyBytes = RandomNumberGenerator.GetBytes(byteLength);
        return Convert.ToBase64String(keyBytes);
    }
}
