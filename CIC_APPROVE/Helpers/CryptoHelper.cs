using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

public static class UrlEncryptionHelper
{
    private static readonly string EncryptionKey = "CIC_SECRET_KEY_2026";

    // ✅ ต้อง ≥ 8 bytes
    private static readonly byte[] SaltBytes =
        Encoding.UTF8.GetBytes("CIC_SALT_2026");

    public static string Encrypt(string clearText)
    {
        byte[] clearBytes = Encoding.UTF8.GetBytes(clearText);

        using (Aes aes = Aes.Create())
        {
            var pdb = new Rfc2898DeriveBytes(
                EncryptionKey,
                SaltBytes,
                10000   // iteration
            );

            aes.Key = pdb.GetBytes(32);
            aes.IV = pdb.GetBytes(16);

            using (var ms = new MemoryStream())
            {
                using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
                {
                    cs.Write(clearBytes, 0, clearBytes.Length);
                    cs.Close();
                }

                return Convert.ToBase64String(ms.ToArray())
                    .Replace("+", "-")
                    .Replace("/", "_")
                    .Replace("=", "");
            }
        }
    }

    public static string Decrypt(string cipherText)
    {
        cipherText = cipherText
            .Replace("-", "+")
            .Replace("_", "/");

        switch (cipherText.Length % 4)
        {
            case 2: cipherText += "=="; break;
            case 3: cipherText += "="; break;
        }

        byte[] cipherBytes = Convert.FromBase64String(cipherText);

        using (Aes aes = Aes.Create())
        {
            var pdb = new Rfc2898DeriveBytes(
                EncryptionKey,
                SaltBytes,
                10000
            );

            aes.Key = pdb.GetBytes(32);
            aes.IV = pdb.GetBytes(16);

            using (var ms = new MemoryStream())
            {
                using (var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Write))
                {
                    cs.Write(cipherBytes, 0, cipherBytes.Length);
                    cs.Close();
                }

                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }
    }
}