using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace FlowOS.Infrastructure.Services;

/// <summary>
/// CR-15: passphrase encryption for tenant backup files. A backup holds the tenant's AI provider settings,
/// running-instance payloads and the event log, so the file must be unreadable without the passphrase.
/// AES-256-GCM with a PBKDF2-SHA256 key (random salt and nonce). The header fields are bound into the
/// authentication tag, so they cannot be changed without failing decryption. The passphrase is never stored.
/// </summary>
public static class TenantBackupEncryption
{
    public const string Format = "flowos-tenant-backup-encrypted";
    public const int FormatVersion = 1;
    public const string Kdf = "PBKDF2-SHA256";
    public const string Cipher = "AES-256-GCM";
    public const int Iterations = 600_000;
    public const int MinimumPassphraseLength = 12;
    public const string PassphraseHeader = "X-FlowOS-Backup-Passphrase";

    // Bounds accepted on restore, so a crafted file cannot make the server spin for minutes.
    private const int MinIterations = 100_000;
    private const int MaxIterations = 5_000_000;

    public static bool IsEncrypted(string json)
    {
        try
        {
            return JsonNode.Parse(json)?["format"]?.ToString() == Format;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }

    public static string Encrypt(string backupJson, string passphrase)
    {
        EnsureStrongEnough(passphrase);

        var plain = JsonNode.Parse(backupJson) ?? throw new ArgumentException("Backup is empty.", nameof(backupJson));
        var flowOsVersion = plain["flowOsVersion"]?.ToString() ?? string.Empty;
        var takenAtUtc = plain["takenAtUtc"]?.ToString() ?? string.Empty;

        var salt = RandomNumberGenerator.GetBytes(16);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var key = DeriveKey(passphrase, salt, Iterations);
        var plaintext = Encoding.UTF8.GetBytes(backupJson);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];

        using (var aes = new AesGcm(key, tag.Length))
            aes.Encrypt(nonce, plaintext, ciphertext, tag, AssociatedData(Iterations, salt, flowOsVersion, takenAtUtc));
        CryptographicOperations.ZeroMemory(key);

        return new JsonObject
        {
            ["format"] = Format,
            ["formatVersion"] = FormatVersion,
            ["cipher"] = Cipher,
            ["kdf"] = Kdf,
            ["iterations"] = Iterations,
            ["salt"] = Convert.ToBase64String(salt),
            ["nonce"] = Convert.ToBase64String(nonce),
            ["tag"] = Convert.ToBase64String(tag),
            ["flowOsVersion"] = flowOsVersion,
            ["takenAtUtc"] = takenAtUtc,
            ["ciphertext"] = Convert.ToBase64String(ciphertext)
        }.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }

    public static string Decrypt(string encryptedJson, string? passphrase)
    {
        if (string.IsNullOrEmpty(passphrase))
            throw new TenantBackupException("BACKUP-PASSPHRASE-REQUIRED",
                $"This backup is encrypted. Send its passphrase in the {PassphraseHeader} header.", 400);

        JsonNode envelope;
        byte[] salt, nonce, tag, ciphertext;
        int iterations;
        try
        {
            envelope = JsonNode.Parse(encryptedJson) ?? throw new FormatException();
            if (envelope["formatVersion"]?.GetValue<int>() != FormatVersion
                || envelope["cipher"]?.ToString() != Cipher
                || envelope["kdf"]?.ToString() != Kdf)
                throw new TenantBackupException("BACKUP-FORMAT", "Unsupported encrypted backup version.", 400);
            iterations = envelope["iterations"]!.GetValue<int>();
            salt = Convert.FromBase64String(envelope["salt"]!.ToString());
            nonce = Convert.FromBase64String(envelope["nonce"]!.ToString());
            tag = Convert.FromBase64String(envelope["tag"]!.ToString());
            ciphertext = Convert.FromBase64String(envelope["ciphertext"]!.ToString());
        }
        catch (TenantBackupException)
        {
            throw;
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or NullReferenceException or System.Text.Json.JsonException)
        {
            throw new TenantBackupException("BACKUP-FORMAT", "The encrypted backup file is damaged.", 400, ex);
        }

        if (iterations is < MinIterations or > MaxIterations || salt.Length != 16 || nonce.Length != 12 || tag.Length != 16)
            throw new TenantBackupException("BACKUP-FORMAT", "The encrypted backup file has invalid parameters.", 400);

        var key = DeriveKey(passphrase, salt, iterations);
        var plaintext = new byte[ciphertext.Length];
        try
        {
            using var aes = new AesGcm(key, tag.Length);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, AssociatedData(
                iterations, salt, envelope["flowOsVersion"]?.ToString() ?? string.Empty, envelope["takenAtUtc"]?.ToString() ?? string.Empty));
        }
        catch (CryptographicException ex)
        {
            throw new TenantBackupException("BACKUP-DECRYPT-FAILED",
                "The passphrase is wrong, or the file was changed after it was encrypted.", 400, ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }

        return Encoding.UTF8.GetString(plaintext);
    }

    public static void EnsureStrongEnough(string? passphrase)
    {
        if (string.IsNullOrWhiteSpace(passphrase) || passphrase.Length < MinimumPassphraseLength)
            throw new TenantBackupException("BACKUP-PASSPHRASE-WEAK",
                $"Use a backup passphrase of at least {MinimumPassphraseLength} characters.", 400);
    }

    private static byte[] DeriveKey(string passphrase, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(passphrase), salt, iterations, HashAlgorithmName.SHA256, 32);

    private static byte[] AssociatedData(int iterations, byte[] salt, string flowOsVersion, string takenAtUtc) =>
        Encoding.UTF8.GetBytes($"{Format}|{FormatVersion}|{Cipher}|{Kdf}|{iterations}|{Convert.ToBase64String(salt)}|{flowOsVersion}|{takenAtUtc}");
}
