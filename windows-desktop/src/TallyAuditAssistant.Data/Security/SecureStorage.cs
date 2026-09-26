using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.Data.Security;

public class SecureStorage : ISecureStorage
{
    private readonly string _storageFilePath;
    private readonly ILogger<SecureStorage> _logger;
    private readonly byte[] _entropy = Encoding.UTF8.GetBytes("TallyAuditAssistant.SecureStorage.Entropy.v1");
    private readonly object _lock = new();

    public SecureStorage(string storageDirectory, ILogger<SecureStorage> logger)
    {
        _logger = logger;
        if (!Directory.Exists(storageDirectory))
        {
            Directory.CreateDirectory(storageDirectory);
        }
        _storageFilePath = Path.Combine(storageDirectory, "credentials_secure.dat");
    }

    public void SetSecret(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be empty.", nameof(key));

        lock (_lock)
        {
            var dictionary = LoadDecryptedDictionary();
            dictionary[key] = value;
            SaveEncryptedDictionary(dictionary);
        }
    }

    public string? GetSecret(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;

        lock (_lock)
        {
            var dictionary = LoadDecryptedDictionary();
            return dictionary.TryGetValue(key, out var val) ? val : null;
        }
    }

    public void RemoveSecret(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return;

        lock (_lock)
        {
            var dictionary = LoadDecryptedDictionary();
            if (dictionary.Remove(key))
            {
                SaveEncryptedDictionary(dictionary);
            }
        }
    }

    public bool HasSecret(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return false;

        lock (_lock)
        {
            var dictionary = LoadDecryptedDictionary();
            return dictionary.ContainsKey(key);
        }
    }

    private Dictionary<string, string> LoadDecryptedDictionary()
    {
        if (!File.Exists(_storageFilePath))
            return new Dictionary<string, string>();

        try
        {
            byte[] encryptedBytes = File.ReadAllBytes(_storageFilePath);
            if (encryptedBytes.Length == 0)
                return new Dictionary<string, string>();

            byte[] decryptedBytes;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                decryptedBytes = ProtectedData.Unprotect(encryptedBytes, _entropy, DataProtectionScope.CurrentUser);
            }
            else
            {
                // Fallback for non-Windows dev / container testing
                decryptedBytes = FallbackDecrypt(encryptedBytes);
            }

            string json = Encoding.UTF8.GetString(decryptedBytes);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to decrypt local secure storage file. Reinitializing.");
            return new Dictionary<string, string>();
        }
    }

    private void SaveEncryptedDictionary(Dictionary<string, string> dictionary)
    {
        try
        {
            string json = JsonSerializer.Serialize(dictionary);
            byte[] plainBytes = Encoding.UTF8.GetBytes(json);

            byte[] encryptedBytes;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                encryptedBytes = ProtectedData.Protect(plainBytes, _entropy, DataProtectionScope.CurrentUser);
            }
            else
            {
                encryptedBytes = FallbackEncrypt(plainBytes);
            }

            // Write atomically via temporary file
            string tempFile = _storageFilePath + ".tmp";
            File.WriteAllBytes(tempFile, encryptedBytes);
            File.Move(tempFile, _storageFilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save encrypted credentials to secure storage.");
            throw;
        }
    }

    private static byte[] FallbackEncrypt(byte[] data)
    {
        using var aes = Aes.Create();
        aes.Key = SHA256.HashData(Encoding.UTF8.GetBytes("TallyAuditAssistant.FallbackMachineKey"));
        aes.GenerateIV();

        using var ms = new MemoryStream();
        ms.Write(aes.IV, 0, aes.IV.Length);

        using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
        {
            cs.Write(data, 0, data.Length);
            cs.FlushFinalBlock();
        }

        return ms.ToArray();
    }

    private static byte[] FallbackDecrypt(byte[] encryptedData)
    {
        using var aes = Aes.Create();
        aes.Key = SHA256.HashData(Encoding.UTF8.GetBytes("TallyAuditAssistant.FallbackMachineKey"));

        byte[] iv = new byte[aes.BlockSize / 8];
        Array.Copy(encryptedData, 0, iv, 0, iv.Length);
        aes.IV = iv;

        using var ms = new MemoryStream();
        using (var cs = new CryptoStream(new MemoryStream(encryptedData, iv.Length, encryptedData.Length - iv.Length),
                   aes.CreateDecryptor(), CryptoStreamMode.Read))
        {
            cs.CopyTo(ms);
        }

        return ms.ToArray();
    }
}
