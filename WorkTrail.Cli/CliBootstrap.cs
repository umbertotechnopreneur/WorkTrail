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


using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using WorkTrail.Application;
using WorkTrail.Runtime;

namespace WorkTrail.Cli;

/// <summary>Initializes console encoding, cancellation, composition, and the shared command router.</summary>
public static class CliBootstrap
{
    /// <summary>Runs the CLI frontend without constructing application infrastructure in commands or renderers.</summary>
    public static async Task<int> RunAsync(string[] arguments, string executablePath, CancellationToken applicationCancellationToken = default)
    {
        NativeConsole.TryAttachParentConsole();
        ConfigureUtf8Console();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(applicationCancellationToken);
        Console.CancelKeyPress += OnCancel;
        try
        {
            var stripped = arguments.Where(x => !string.Equals(x, "-cli", StringComparison.OrdinalIgnoreCase) && !string.Equals(x, "--cli", StringComparison.OrdinalIgnoreCase)).ToArray();
            CliOptions options;
            try
            {
                options = CliOptions.Parse(stripped, Console.IsOutputRedirected || Console.IsErrorRedirected);
            }
            catch (ArgumentException)
            {
                Console.Error.WriteLine(CliStrings.Get(ResolveDiagnosticLanguage(stripped), "error.invalidGlobalOption"));
                return 2;
            }

            var output = new CliOutput(options);
            var commandArguments = CliCommandCatalog.Normalize(options.CommandArguments);

            if (options.Format == CliFormat.Rich && !IsPowerShell7Parent())
            {
                output.WriteDiagnostic(output.Text("diagnostic.pwsh"));
            }

            // Even help/version require the runtime-owned Premium decision before CLI dispatch.
            await using var application = await RuntimeConnector.ConnectAsync(executablePath, options.TimeoutSeconds, cancellation.Token);
            if (application is null)
            {
                output.WriteResult(OperationResult<object>.Failure("runtime.unavailable", "RuntimeUnavailable"));
                return 4;
            }

            var router = new CliRouter(application, output, options);
            return await router.RunAsync(commandArguments, cancellation.Token);
        }
        finally
        {
            Console.CancelKeyPress -= OnCancel;
        }

        void OnCancel(object? sender, ConsoleCancelEventArgs eventArgs)
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        }
    }

    /// <summary>Creates the stable version result shared by one-shot and interactive modes.</summary>
    internal static OperationResult<object> CreateVersionResult() =>
        OperationResult<object>.Success(
            "version.loaded",
            "VersionLoaded",
            new
            {
                productVersion = typeof(CliBootstrap).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
                protocolVersion = RuntimeProtocol.ProtocolVersion
            });

    private static void ConfigureUtf8Console()
    {
        try
        {
            Console.InputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            NativeConsole.SetConsoleOutputCP(65001);
            NativeConsole.SetConsoleCP(65001);
        }
        catch
        {
            // A redirected/non-Windows host can reject console configuration; plain output remains available.
        }
    }

    private static bool IsPowerShell7Parent()
    {
        try
        {
            using var parent = Process.GetProcessById(NativeConsole.GetParentProcessId());
            return string.Equals(parent.ProcessName, "pwsh", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string ResolveDiagnosticLanguage(IReadOnlyList<string> arguments)
    {
        var index = arguments.ToList().FindIndex(argument => argument.Equals("--language", StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index + 1 >= arguments.Count)
        {
            return "system";
        }

        var requested = arguments[index + 1];
        return CliOptions.SupportedLanguages.FirstOrDefault(language => language.Equals(requested, StringComparison.OrdinalIgnoreCase))
            ?? "system";
    }
}

/// <summary>Contains minimal Windows console interop used only by the CLI composition root.</summary>
internal static class NativeConsole
{
    private const uint AttachParentProcess = 0xFFFFFFFF;

    /// <summary>Attempts to attach a GUI-subsystem process to the parent terminal.</summary>
    internal static void TryAttachParentConsole()
    {
        if (OperatingSystem.IsWindows())
        {
            _ = AttachConsole(AttachParentProcess);
        }
    }

    /// <summary>Gets the parent process identifier for supported-shell detection.</summary>
    internal static int GetParentProcessId()
    {
        var entry = new PROCESS_BASIC_INFORMATION();
        var status = NtQueryInformationProcess(Process.GetCurrentProcess().Handle, 0, ref entry, Marshal.SizeOf<PROCESS_BASIC_INFORMATION>(), out _);
        return status == 0 ? entry.InheritedFromUniqueProcessId.ToInt32() : 0;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(uint dwProcessId);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetConsoleOutputCP(uint wCodePageID);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetConsoleCP(uint wCodePageID);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr processHandle, int processInformationClass, ref PROCESS_BASIC_INFORMATION processInformation, int processInformationLength, out int returnLength);

    private struct PROCESS_BASIC_INFORMATION
    {
        public IntPtr Reserved1;
        public IntPtr PebBaseAddress;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public IntPtr[] Reserved2;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }
}
