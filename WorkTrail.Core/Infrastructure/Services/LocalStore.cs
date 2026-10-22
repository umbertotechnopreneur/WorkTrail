// SPDX-License-Identifier: MIT
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WorkTrail.Application;

namespace WorkTrail.Services;

/// <summary>Persists widget settings without opening or changing existing activity databases.</summary>
public sealed class LocalStore
{

    #region Fields
    private readonly UtilityService _utilities = new();
    private readonly string _settingsPath;
    private readonly string _settingsBootstrapMutexName;
    private readonly object _fileLock = new();
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    #endregion

    #region Initialization

    /// <summary>Opens only the existing preferences file, optionally in an isolated directory.</summary>
    public LocalStore(string? dataDirectory = null)
    {
        var directory = string.IsNullOrWhiteSpace(dataDirectory) ? _utilities.AppDataDirectory : Path.GetFullPath(dataDirectory);
        Directory.CreateDirectory(directory);
        _settingsPath = Path.Combine(directory, "appsettings.json");
        var normalizedPathBytes = Encoding.UTF8.GetBytes(_settingsPath.ToUpperInvariant());
        var pathDigest = SHA256.HashData(normalizedPathBytes);
        var fingerprint = Convert.ToHexString(pathDigest)[..32];
        _settingsBootstrapMutexName = $"Local\\WorkTrail.Settings.{fingerprint}";
    }

    #endregion

    #region Methods and properties

    /// <summary>
    /// Loads application settings and rejects malformed persisted configuration.
    /// </summary>
    public AppSettings LoadSettings() => WithSettingsMutex(() =>
    {
        var settings = new AppSettings();
        if (File.Exists(_settingsPath))
        {
            var document = JsonSerializer.Deserialize<JsonObject>(File.ReadAllText(_settingsPath), _json)
                ?? throw new InvalidOperationException("The WorkTrail settings file must contain a JSON object.");

            settings = document.Deserialize<AppSettings>(_json)
                ?? throw new InvalidOperationException("The WorkTrail settings file must contain a JSON object.");
        }

        var normalized = SettingsCatalog.NormalizePersisted(settings);
        return EnsureInstallationId(normalized);
    });

    /// <summary>
    /// Reads the persisted installation identifier without creating or rewriting settings.
    /// </summary>
    /// <returns>The existing installation identifier, or null when settings have not been initialized by the runtime.</returns>
    public string? TryLoadInstallationId()
    {
        if (!File.Exists(_settingsPath))
        {
            return null;
        }

        var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_settingsPath), _json)
            ?? throw new InvalidOperationException("The WorkTrail settings file must contain a JSON object.");
        if (!IsValidInstallationId(settings.InstallationId))
        {
            throw new InvalidOperationException("The WorkTrail settings file has an invalid installation identity.");
        }

        return settings.InstallationId;
    }

    /// <summary>
    /// Persists application settings to JSON file.
    /// </summary>
    /// <param name="settings">Settings payload.</param>
    public void SaveSettings(AppSettings settings) => WithSettingsMutex(() =>
    {
        var normalized = SettingsCatalog.NormalizePersisted(settings);
        var payload = EnsureInstallationId(normalized);
        WriteSettingsFile(payload);
        return true;
    });

    #endregion

    #region Implementation

    /// <summary>Writes a fully normalized settings snapshot using an atomic same-directory replacement.</summary>
    private void WriteSettingsFile(AppSettings payload)
    {
        var temporaryPath = _settingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        lock (_fileLock)
        {
            // Write and flush in the target directory so a completed replacement is never a torn JSON file.
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(JsonSerializer.Serialize(payload, _json));
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            try
            {
                if (File.Exists(_settingsPath))
                {
                    File.Replace(temporaryPath, _settingsPath, null, ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(temporaryPath, _settingsPath);
                }
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
    }

    /// <summary>Reuses or creates the installation identity under the per-settings-file mutex.</summary>
    private AppSettings EnsureInstallationId(AppSettings settings)
    {
        if (IsValidInstallationId(settings.InstallationId))
        {
            return settings;
        }

        using var bootstrapMutex = new Mutex(false, _settingsBootstrapMutexName);
        var acquired = false;
        try
        {
            try
            {
                acquired = bootstrapMutex.WaitOne(TimeSpan.FromSeconds(10));
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (!acquired)
            {
                throw new TimeoutException("Timed out while initializing the WorkTrail installation identity.");
            }

            var persistedInstallationId = TryLoadInstallationId();
            if (persistedInstallationId is not null)
            {
                return settings with { InstallationId = persistedInstallationId };
            }

            var initialized = settings with { InstallationId = _utilities.GenerateInstallationId() };
            // The fixed per-settings-file mutex makes first-launch identity initialization process-safe.
            WriteSettingsFile(initialized);
            return initialized;
        }
        finally
        {
            if (acquired)
            {
                bootstrapMutex.ReleaseMutex();
            }
        }
    }

    /// <summary>Serializes settings reads and replacements across processes with a bounded wait.</summary>
    private T WithSettingsMutex<T>(Func<T> operation)
    {
        using var settingsMutex = new Mutex(false, _settingsBootstrapMutexName);
        var acquired = false;
        try
        {
            try
            {
                acquired = settingsMutex.WaitOne(TimeSpan.FromSeconds(10));
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (!acquired)
            {
                throw new TimeoutException("Timed out while accessing WorkTrail settings.");
            }

            // Read/replace operations share one process-safe mutex so readers never observe an in-progress settings write.
            return operation();
        }
        finally
        {
            if (acquired)
            {
                settingsMutex.ReleaseMutex();
            }
        }
    }

    /// <summary>Checks the bounded identifier alphabet accepted by the runtime.</summary>
    private static bool IsValidInstallationId(string? value) =>
        value is { Length: >= 16 and <= 160 }
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '.');

    #endregion

}
