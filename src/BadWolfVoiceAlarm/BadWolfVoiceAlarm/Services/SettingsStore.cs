using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BadWolfVoiceAlarm.Models;

namespace BadWolfVoiceAlarm.Services;

public sealed class SettingsStore
{
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BadWolfVoiceAlarm");

    private static readonly string SettingsPath = Path.Combine(SettingsDirectory, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public AppSettings Load()
    {
        if (!File.Exists(SettingsPath))
        {
            return new AppSettings();
        }

        return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), JsonOptions)
            ?? new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(SettingsDirectory);
        string tempPath = SettingsPath + ".tmp";
        try
        {
            File.WriteAllText(tempPath, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(tempPath, SettingsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    public static string EncryptToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return string.Empty;
        }

        byte[] encrypted = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(token.Trim()), null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    public static string DecryptToken(string encryptedToken)
    {
        if (string.IsNullOrWhiteSpace(encryptedToken))
        {
            return string.Empty;
        }

        byte[] clearText = ProtectedData.Unprotect(
            Convert.FromBase64String(encryptedToken), null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(clearText);
    }
}
