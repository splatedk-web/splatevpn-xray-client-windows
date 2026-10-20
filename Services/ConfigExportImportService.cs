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
        var subscriptions = _storage.LoadSubscriptions();
        var settings = _storage.LoadSettings();
        var payload = new ConfigPayload { Servers = servers, Subscriptions = subscriptions, Settings = settings };
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        var encrypted = Encrypt(json, passphrase);
        await File.WriteAllBytesAsync(filePath, encrypted);
    }

    public async Task ImportAsync(string filePath, string passphrase)
    {
        var encrypted = await File.ReadAllBytesAsync(filePath);
        var json = Decrypt(encrypted, passphrase);
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("Servers", out var serversElement) || serversElement.ValueKind != JsonValueKind.Array ||
            !document.RootElement.TryGetProperty("Subscriptions", out var subscriptionsElement) || subscriptionsElement.ValueKind != JsonValueKind.Array ||
            !document.RootElement.TryGetProperty("Settings", out var settingsElement) || settingsElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Файл не содержит полной резервной копии SplateVPN.");
        var payload = JsonSerializer.Deserialize<ConfigPayload>(json)
            ?? throw new InvalidDataException("Не удалось прочитать резервную копию.");
        if (payload.Servers.Any(n => n == null || string.IsNullOrWhiteSpace(n.Address) || n.Port is < 1 or > 65535) ||
            payload.Subscriptions.Any(s => s == null) || payload.Settings.HttpPort is < 1 or > 65535 ||
            payload.Settings.SocksPort is < 1 or > 65535 || payload.Settings.HttpPort == payload.Settings.SocksPort)
            throw new InvalidDataException("В резервной копии некорректные серверы или порты.");
        try { payload.Settings.AdBlockAllowlist = string.Join('\n', AdFilterService.Allowlist(payload.Settings.AdBlockAllowlist)); }
        catch (ArgumentException error) { throw new InvalidDataException("Некорректные исключения блокировщика в резервной копии.", error); }
        SettingsCompatibility.Normalize(payload.Settings);
        _storage.SaveBackup(payload.Servers, payload.Subscriptions, payload.Settings);
    }

    private static byte[] Encrypt(string plainText, string passphrase)
    {
        if (string.IsNullOrWhiteSpace(passphrase) || passphrase.Length < 8)
            throw new ArgumentException("Пароль резервной копии должен содержать минимум 8 символов.");
        var salt = RandomNumberGenerator.GetBytes(16);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plain = Encoding.UTF8.GetBytes(plainText);
        var result = new byte[52 + plain.Length];
        Header.CopyTo(result, 0); salt.CopyTo(result, 8); nonce.CopyTo(result, 24);
        var key = Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, 100_000, HashAlgorithmName.SHA256, 32);
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Encrypt(nonce, plain, result.AsSpan(52), result.AsSpan(36, 16), result.AsSpan(0, 36));
            return result;
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    private static readonly byte[] Header = "SPCFG002"u8.ToArray();
    public static bool IsLegacyBackup(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        Span<byte> header = stackalloc byte[8];
        return stream.Read(header) != 8 || !header.SequenceEqual(Header);
    }

    private static string Decrypt(byte[] data, string passphrase)
    {
        try
        {
            if (!data.AsSpan().StartsWith(Header))
                return DecryptLegacy(data, string.IsNullOrEmpty(passphrase) ? "splate_pass" : passphrase);
            if (data.Length < 52) throw new InvalidDataException("Файл резервной копии обрезан.");
            var key = Rfc2898DeriveBytes.Pbkdf2(passphrase, data.AsSpan(8, 16), 100_000, HashAlgorithmName.SHA256, 32);
            try
            {
                var plain = new byte[data.Length - 52];
                using var aes = new AesGcm(key, 16);
                aes.Decrypt(data.AsSpan(24, 12), data.AsSpan(52), data.AsSpan(36, 16), plain, data.AsSpan(0, 36));
                return Encoding.UTF8.GetString(plain);
            }
            finally { CryptographicOperations.ZeroMemory(key); }
        }
        catch (CryptographicException ex)
        { throw new InvalidDataException("Неверный пароль или повреждённая резервная копия.", ex); }
    }

    private static string DecryptLegacy(byte[] cipherWithIv, string passphrase)
    {
        if (cipherWithIv.Length < 32 || cipherWithIv.Length % 16 != 0)
            throw new InvalidDataException("Файл резервной копии повреждён или имеет другой формат.");
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
        public List<SubscriptionItem> Subscriptions { get; set; } = new();
        public AppSettings Settings { get; set; } = new();
    }
}
