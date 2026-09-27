using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using SplateClientDesktop.Models;

namespace SplateClientDesktop.Services;

public class ConfigExportImportService : IConfigExportImportService
{
    private readonly IServerStorage _storage;

    public ConfigExportImportService(IServerStorage storage)
    {
        _storage = storage;
    }

    public async Task ExportAsync(string filePath, string passphrase)
    {
        var servers = _storage.LoadServers();
        var settings = _storage.LoadSettings();
        var payload = new ConfigPayload { Servers = servers, Settings = settings };
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        var encrypted = Encrypt(json, passphrase);
        await File.WriteAllBytesAsync(filePath, encrypted);
    }

    public async Task ImportAsync(string filePath, string passphrase)
    {
        var encrypted = await File.ReadAllBytesAsync(filePath);
        var json = Decrypt(encrypted, passphrase);
        var payload = JsonSerializer.Deserialize<ConfigPayload>(json);
        if (payload?.Servers != null)
            _storage.SaveServers(payload.Servers);
        if (payload?.Settings != null)
            _storage.SaveSettings(payload.Settings);
    }

    private static byte[] Encrypt(string plainText, string passphrase)
    {
        using var aes = Aes.Create();
        var key = DeriveKey(passphrase, aes.KeySize / 8);
        aes.Key = key;
        aes.GenerateIV();
        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
        // Prepend IV to cipher
        var result = new byte[aes.IV.Length + cipherBytes.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
        Buffer.BlockCopy(cipherBytes, 0, result, aes.IV.Length, cipherBytes.Length);
        return result;
    }

    private static string Decrypt(byte[] cipherWithIv, string passphrase)
    {
        using var aes = Aes.Create();
        var key = DeriveKey(passphrase, aes.KeySize / 8);
        aes.Key = key;
        var iv = new byte[aes.BlockSize / 8];
        Buffer.BlockCopy(cipherWithIv, 0, iv, 0, iv.Length);
        aes.IV = iv;
        using var decryptor = aes.CreateDecryptor();
        var cipherBytes = new byte[cipherWithIv.Length - iv.Length];
        Buffer.BlockCopy(cipherWithIv, iv.Length, cipherBytes, 0, cipherBytes.Length);
        var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
        return Encoding.UTF8.GetString(plainBytes);
    }

    private static byte[] DeriveKey(string passphrase, int keyLength)
    {
        var salt = Encoding.UTF8.GetBytes("SplateVPN_Salt_2025");
        using var derive = new Rfc2898DeriveBytes(passphrase, salt, 100_000, HashAlgorithmName.SHA256);
        return derive.GetBytes(keyLength);
    }

    private class ConfigPayload
    {
        public List<ServerNode> Servers { get; set; } = new();
        public AppSettings Settings { get; set; } = new();
    }
}
