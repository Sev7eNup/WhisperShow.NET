using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace Voxwright.Core.Services.Configuration;

/// <summary>
/// Encrypts and decrypts API keys using Windows DPAPI (Data Protection API).
/// Keys are encrypted with <see cref="DataProtectionScope.CurrentUser"/> so only
/// the same Windows user account can decrypt them.
///
/// Encrypted values are stored with a "DPAPI:" prefix to distinguish them from plaintext.
/// This allows transparent migration: plaintext keys are read as-is and will be encrypted
/// on the next save.
/// </summary>
[SupportedOSPlatform("windows")]
public static class ApiKeyProtector
{
    private const string Prefix = "DPAPI:";

    /// <summary>
    /// Encrypts a plaintext API key using DPAPI. Returns null/empty values unchanged.
    /// Already-encrypted values (with "DPAPI:" prefix) are returned unchanged.
    /// </summary>
    public static string? Protect(string? plainKey)
    {
        if (string.IsNullOrWhiteSpace(plainKey) || plainKey.StartsWith(Prefix, StringComparison.Ordinal))
            return plainKey;

        var bytes = Encoding.UTF8.GetBytes(plainKey);
        var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        return Prefix + Convert.ToBase64String(encrypted);
    }

    /// <summary>
    /// Decrypts a DPAPI-encrypted API key. Returns plaintext values unchanged (for migration).
    /// Returns null/empty values unchanged.
    /// </summary>
    public static string? Unprotect(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.StartsWith(Prefix, StringComparison.Ordinal))
            return value;

        try
        {
            var encrypted = Convert.FromBase64String(value[Prefix.Length..]);
            var decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            // Key was encrypted by a different user, corrupted, or has invalid Base64
            return null;
        }
    }

    /// <summary>
    /// Returns true if the value is a DPAPI-encrypted string.
    /// </summary>
    public static bool IsProtected(string? value) =>
        value is not null && value.StartsWith(Prefix, StringComparison.Ordinal);
}
