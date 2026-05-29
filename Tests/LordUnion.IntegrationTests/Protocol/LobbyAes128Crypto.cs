using System.Security.Cryptography;
using System.Text;

namespace LordUnion.IntegrationTests.Protocol;

public static class LobbyAes128Crypto
{
    public const string DefaultKey = "1kHL@65J";

    public static string EncryptToHex(string plainText, string key)
    {
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var encrypted = Encrypt(plainBytes, key);
        return Encoding.UTF8.GetString(ToUpperHex(encrypted));
    }

    public static string DecryptFromHex(string hexCipherText, string key)
    {
        var cipherBytes = ToHex(Encoding.UTF8.GetBytes(hexCipherText));
        var plainBytes = Decrypt(cipherBytes, key);
        return Encoding.UTF8.GetString(plainBytes);
    }

    private static byte[] Encrypt(byte[] data, string key)
    {
        using var aes = Aes.Create();
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.PKCS7;
        aes.KeySize = 128;
        aes.Key = GetInnerKey(Encoding.UTF8.GetBytes(key));

        using var encryptor = aes.CreateEncryptor();
        return encryptor.TransformFinalBlock(data, 0, data.Length);
    }

    private static byte[] Decrypt(byte[] data, string key)
    {
        using var aes = Aes.Create();
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.PKCS7;
        aes.KeySize = 128;
        aes.Key = GetInnerKey(Encoding.UTF8.GetBytes(key));

        using var decryptor = aes.CreateDecryptor();
        return decryptor.TransformFinalBlock(data, 0, data.Length);
    }

    private static byte[] GetInnerKey(byte[] key)
    {
        var innerKey = new byte[16];
        innerKey[0] = 0x41;
        innerKey[1] = 0x39;
        innerKey[2] = 0x68;
        innerKey[3] = 0x2A;
        innerKey[4] = 0x6E;
        innerKey[5] = 0x36;
        innerKey[6] = 0x37;
        innerKey[7] = 0x24;

        for (var i = 0; i < 8; i++)
        {
            innerKey[i + 8] = i < key.Length ? key[i] : (byte)0;
        }

        return innerKey;
    }

    private static byte[] ToUpperHex(byte[] bytes)
    {
        const string hex = "0123456789ABCDEF";
        var result = new byte[bytes.Length * 2];
        var index = 0;
        foreach (var value in bytes)
        {
            result[index++] = (byte)hex[(value >> 4) & 0x0F];
            result[index++] = (byte)hex[value & 0x0F];
        }

        return result;
    }

    private static byte[] ToHex(byte[] charBytes)
    {
        var output = new byte[charBytes.Length / 2];
        for (var i = 0; i < output.Length; i++)
        {
            var high = HexNibble(charBytes[2 * i]);
            var low = HexNibble(charBytes[(2 * i) + 1]);
            output[i] = (byte)((high << 4) + low);
        }

        return output;
    }

    private static int HexNibble(byte value)
    {
        var ch = value is >= (byte)'a' and <= (byte)'z' ? value - 32 : value;
        var digit = ch - (byte)'0';
        if (digit > 9)
        {
            digit -= 7;
        }

        return digit;
    }
}