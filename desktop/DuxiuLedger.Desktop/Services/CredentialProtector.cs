using System.Security.Cryptography;
using System.Text;

namespace DuxiuLedger.Desktop.Services;

public static class CredentialProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("DuxiuLedger.MySql.v1");

    public static string Protect(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return "";
        return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plainText), Entropy, DataProtectionScope.CurrentUser));
    }

    public static string Unprotect(string cipherText)
    {
        if (string.IsNullOrWhiteSpace(cipherText)) return "";
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(cipherText), Entropy, DataProtectionScope.CurrentUser)); }
        catch (CryptographicException) { return ""; }
    }
}
