using System.Security.Cryptography;
using System.Text;
using Sodium;

namespace BoltZip.Core.Bz;

/// <summary>
/// Key derivation for the <c>.bz</c> format. Uses Argon2id (memory-hard) to turn a
/// password into a master key, then HKDF-SHA256 to split domain-separated sub-keys for
/// the index and the content streams so each can safely use its own nonce space.
/// </summary>
internal static class BzCrypto
{
    public static byte[] DeriveMasterKey(string password, byte[] salt, long opsLimit, int memLimit)
    {
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        try
        {
            return PasswordHash.ArgonHashBinary(
                passwordBytes,
                salt,
                opsLimit,
                memLimit,
                BzFormat.KeyBytes,
                PasswordHash.ArgonAlgorithm.Argon_2ID13);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
    }

    public static byte[] DeriveSubKey(byte[] masterKey, string info)
    {
        return HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            masterKey,
            outputLength: BzFormat.KeyBytes,
            salt: null,
            info: Encoding.UTF8.GetBytes(info));
    }
}
