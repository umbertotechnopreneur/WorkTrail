// SPDX-License-Identifier: MIT
/* VBWR B
 *
 * Project: WorkTrail
 * Repository: https://github.com/umbertotechnopreneur/WorkTrail
 * Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
 *
 * VibeWare: Human intent, AI execution, and plenty of tokens
 * Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
 *
 * Modified with AI: OpenAI Codex; added this header on 2026-10-10.
 * Human guidance: Umberto Giacobbi; requested VibeWare branding.
 *
 * Copyright (c) 2026 Umberto Giacobbi
 * License: MIT - see LICENSE
 *
 * VBWR E */


using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32;

namespace WorkTrail.Services;

/// <summary>Installs the pinned official driver only during an explicit sensor activation.</summary>
internal sealed class PawnIoInstaller
{
    private static readonly (Version Version, string Sha256) Distribution = ReadDistribution();
    private readonly string _installerPath;
    private readonly Func<Version?> _readVersion;
    private readonly Func<CancellationToken, Task<int>> _install;
    private Task<int>? _installation;
    private bool _restartRequired;

    internal PawnIoInstaller(string installerPath, Func<Version?>? readVersion = null,
        Func<CancellationToken, Task<int>>? install = null)
    {
        _installerPath = Path.GetFullPath(installerPath);
        _readVersion = readVersion ?? ReadInstalledVersion;
        _install = install ?? InstallBundledAsync;
    }

    /// <summary>Checks the installed prerequisite without starting setup or accepting a pending restart.</summary>
    internal bool CanActivateWithoutSetup => !_restartRequired && _installation is null
        && _readVersion() is { } installed && installed >= Distribution.Version;

    internal async Task EnsureInstalledAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_restartRequired) throw new PawnIoSetupException("HardwareAdvancedSetupRestartRequired");
        // Retain an equal or newer shared installation; never downgrade another application's driver.
        if (_installation is null && _readVersion() is { } installed && installed >= Distribution.Version) return;
        // A caller may stop waiting, but an in-progress system installation is retained for the next explicit request.
        var installation = _installation ??= _install(CancellationToken.None);
        int exitCode;
        try { exitCode = await installation.WaitAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _ = installation.ContinueWith(completed => _ = completed.Exception, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            throw;
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            _installation = null;
            throw new PawnIoSetupException("HardwareAdvancedSetupCancelled", exception);
        }
        catch
        {
            _installation = null;
            throw;
        }
        _installation = null;
        // Do not activate sensors until setup has completed successfully, including any required restart.
        if (exitCode == 3010)
        {
            _restartRequired = true;
            throw new PawnIoSetupException("HardwareAdvancedSetupRestartRequired");
        }
        if (exitCode == 1223) throw new PawnIoSetupException("HardwareAdvancedSetupCancelled");
        if (exitCode != 0 || _readVersion() is not { } version || version < Distribution.Version)
            throw new PawnIoSetupException("HardwareAdvancedSetupFailed");
    }

    internal static Version? ReadInstalledVersion()
    {
        // A missing installation is expected; corrupt installation metadata is an explicit failure.
        using var registry = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = registry.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO");
        if (key is null) return null;
        return key.GetValue("DisplayVersion") is string value && Version.TryParse(value, out var version)
            ? version : throw new InvalidDataException("PawnIO installation version is invalid.");
    }

    internal static async Task ValidateInstallerAsync(Stream installer, CancellationToken cancellationToken)
    {
        if (Convert.ToHexString(await SHA256.HashDataAsync(installer, cancellationToken).ConfigureAwait(false)) != Distribution.Sha256)
            throw new InvalidDataException("The bundled PawnIO installer checksum is invalid.");
    }

    private async Task<int> InstallBundledAsync(CancellationToken cancellationToken)
    {
        // Keep a read-only sharing handle open through execution so the validated executable cannot be replaced.
        await using var installer = new FileStream(_installerPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await ValidateInstallerAsync(installer, cancellationToken).ConfigureAwait(false);
        var start = new ProcessStartInfo(_installerPath)
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetDirectoryName(_installerPath)!
        };
        start.ArgumentList.Add("-install");
        start.ArgumentList.Add("-silent");
        // Windows setup may continue after the caller closes its window. Never kill a driver installation;
        // retain its file lock and process handle until it exits, and never start the collector after cancellation.
        using var process = await Task.Run(() => Process.Start(start), cancellationToken).ConfigureAwait(false)
            ?? throw new IOException("PawnIO setup could not be started.");
        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return process.ExitCode;
    }

    private static (Version, string) ReadDistribution()
    {
        using var stream = typeof(PawnIoInstaller).Assembly.GetManifestResourceStream("WorkTrail.PawnIO.distribution.json")
            ?? throw new InvalidDataException("PawnIO distribution metadata is missing.");
        using var document = JsonDocument.Parse(stream);
        var version = Version.Parse(document.RootElement.GetProperty("version").GetString()!);
        var hash = document.RootElement.GetProperty("sha256").GetString()!;
        if (hash.Length != 64 || hash.Any(character => !char.IsAsciiHexDigitUpper(character)))
            throw new InvalidDataException("PawnIO distribution checksum is invalid.");
        return (version, hash);
    }
}

/// <summary>Identifies the user action required after a driver setup outcome.</summary>
internal sealed class PawnIoSetupException(string messageKey, Exception? innerException = null)
    : Exception("PawnIO setup did not make advanced sensors ready.", innerException)
{
    internal string MessageKey { get; } = messageKey;
}
