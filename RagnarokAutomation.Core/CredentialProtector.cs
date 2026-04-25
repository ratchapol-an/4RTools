using System.Security.Cryptography;
using System.Text;
using System.Runtime.Versioning;

namespace RagnarokAutomation.Core;

[SupportedOSPlatform("windows")]
public static class CredentialProtector
{
    public static string Protect(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
        {
            return string.Empty;
        }

        byte[] data = Encoding.UTF8.GetBytes(plainText);
        byte[] protectedData = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedData);
    }

    public static string Unprotect(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText))
        {
            return string.Empty;
        }

        try
        {
            byte[] data = Convert.FromBase64String(cipherText);
            byte[] plainData = ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plainData);
        }
        catch
        {
            return string.Empty;
        }
    }
}
