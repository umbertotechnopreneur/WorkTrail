// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Linq;
using WorkTrail.Runtime;
using Xunit;

namespace WorkTrail.Presentation.Tests;

public sealed class StartupInstanceContractTests
{
    /// <summary>Prevents an async entry-point wrapper from discarding the STA attribute required by WinUI.</summary>
    [Fact]
    public void DesktopStartup_UsesSynchronousStaEntryPoint()
    {
        var program = File.ReadAllText(RepositoryFile("WorkTrail", "Program.cs"));

        Assert.Matches(@"\[STAThread\]\s+public static void Main\(string\[\] arguments\)", program);
        Assert.DoesNotContain("async Task Main", program, StringComparison.Ordinal);
    }

    /// <summary>Checks that secondary launches finish redirection before returning without creating a WinUI runtime.</summary>
    [Fact]
    public void DesktopStartup_RedirectsLongLivedActivationsBeforeCreatingWinUi()
    {
        var project = File.ReadAllText(RepositoryFile("WorkTrail", "WorkTrail.csproj"));
        var program = File.ReadAllText(RepositoryFile("WorkTrail", "Program.cs"));
        var app = File.ReadAllText(RepositoryFile("WorkTrail", "App.xaml.cs"));

        Assert.Contains("DISABLE_XAML_GENERATED_MAIN", project, StringComparison.Ordinal);
        Assert.Contains("AppInstance.FindOrRegisterForKey(MainInstanceKey)", program, StringComparison.Ordinal);
        Assert.Contains("StartupThreadService.CompleteActivationRedirection(", program, StringComparison.Ordinal);
        Assert.Contains("() => mainInstance.RedirectActivationToAsync(activation).AsTask());", program, StringComparison.Ordinal);
        Assert.Contains("return LaunchOptions.Parse(arguments).Mode is not (LaunchMode.Cli or LaunchMode.Help or LaunchMode.Version);", program, StringComparison.Ordinal);
        Assert.True(
            program.IndexOf("RedirectActivationToAsync", StringComparison.Ordinal)
            < program.IndexOf("Application.Start", StringComparison.Ordinal));
        Assert.Contains("mainInstance.Activated += MainInstance_Activated;", program, StringComparison.Ordinal);
        Assert.Contains("HandleRedirectedActivation", app, StringComparison.Ordinal);
        Assert.Contains("_window.ShowFlyout();", app, StringComparison.Ordinal);
        Assert.Contains("WindowsLaunchArguments.Parse(launch.Arguments", program, StringComparison.Ordinal);
        Assert.Contains("case LaunchMode.Ui:", app, StringComparison.Ordinal);
        Assert.Contains("lock (ActivationGate)", program, StringComparison.Ordinal);
        Assert.DoesNotContain("catch (ArgumentException)", program, StringComparison.Ordinal);
    }

    /// <summary>Prevents redirected WinRT payloads from outliving their callback or crossing into the UI dispatcher.</summary>
    [Fact]
    public void RedirectedActivation_QueuesOnlyAnOwnedManagedSnapshot()
    {
        var program = File.ReadAllText(RepositoryFile("WorkTrail", "Program.cs"));
        var app = File.ReadAllText(RepositoryFile("WorkTrail", "App.xaml.cs"));

        var redirectedStart = app.IndexOf("internal void HandleRedirectedActivation(", StringComparison.Ordinal);
        Assert.True(redirectedStart >= 0);
        var redirectedEnd = app.IndexOf("private async void HandleScreenshotNotificationProtocol(", redirectedStart, StringComparison.Ordinal);
        Assert.True(redirectedEnd > redirectedStart);
        var redirected = app[redirectedStart..redirectedEnd];

        Assert.Contains("Queue<RedirectedActivationRequest>", program, StringComparison.Ordinal);
        Assert.Contains("var request = CaptureRedirectedActivation(activation);", program, StringComparison.Ordinal);
        Assert.True(
            program.IndexOf("var request = CaptureRedirectedActivation(activation);", StringComparison.Ordinal)
            < program.IndexOf("PendingActivations.Enqueue(request);", StringComparison.Ordinal));
        Assert.Contains("Array.AsReadOnly(options.RemainingArguments.ToArray())", program, StringComparison.Ordinal);
        Assert.Contains("internal sealed record RedirectedActivationRequest(LaunchOptions Options, ExtendedActivationKind Kind, string? ProtocolUri = null);", program, StringComparison.Ordinal);
        Assert.Contains("protocol.Uri.AbsoluteUri", program, StringComparison.Ordinal);
        Assert.Contains("HandleRedirectedActivation(RedirectedActivationRequest activation)", app, StringComparison.Ordinal);
        Assert.Contains("HandleRedirectedActivationOnUiThread(RedirectedActivationRequest activation)", app, StringComparison.Ordinal);
        Assert.Contains("var options = activation.Options;", app, StringComparison.Ordinal);
        Assert.DoesNotContain("Queue<AppActivationArguments>", program, StringComparison.Ordinal);
        Assert.DoesNotContain("AppActivationArguments", app, StringComparison.Ordinal);
        Assert.DoesNotContain("activation.Data", redirected, StringComparison.Ordinal);
        Assert.Contains("HandleScreenshotNotificationProtocol(activation.ProtocolUri, closeAfterAction: false)", redirected, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--ui --theme dark")]
    [InlineData("\"C:\\Synthetic Apps\\WorkTrail.exe\" --ui --theme dark")]
    public void RedirectedUi_KeepsTheme(string arguments)
    {
        var options = WindowsLaunchArguments.Parse(arguments, "WorkTrail.exe");

        Assert.Equal(LaunchMode.Ui, options.Mode);
        Assert.Equal("dark", options.Theme);
        Assert.Empty(options.RemainingArguments);
    }

    [Theory]
    [InlineData("--ui --paused --start-tracking --language \"it-IT\"")]
    [InlineData("WorkTrail.exe --ui --safe-mode --start-tracking --language \"it-IT\"")]
    public void RedirectedUi_PreservesFlagsThatSuppressAutomaticTracking(string arguments)
    {
        var options = WindowsLaunchArguments.Parse(arguments, "WorkTrail.exe");

        Assert.Equal(LaunchMode.Ui, options.Mode);
        Assert.Equal("it-IT", options.Language);
        Assert.False(TrackingStartupPolicy.ShouldStart(options, new AppSettings(StartTrackingOnLaunch: true)));
    }

    [Theory]
    [InlineData("--background")]
    [InlineData("WorkTrail.exe --background")]
    public void RedirectedBackground_RemainsHeadless(string arguments)
    {
        Assert.Equal(LaunchMode.Background, WindowsLaunchArguments.Parse(arguments, "WorkTrail.exe").Mode);
    }

    [Fact]
    public void EmptyActivation_DoesNotReuseHostArguments()
    {
        var options = WindowsLaunchArguments.Parse(string.Empty, "WorkTrail.exe");

        Assert.Equal(LaunchMode.Ui, options.Mode);
        Assert.Empty(options.RemainingArguments);
    }

    [Fact]
    public void InvalidActivationLanguage_FailsBeforeApplyingDefaults()
    {
        Assert.Throws<ArgumentException>(() => WindowsLaunchArguments.Parse("--language invalid", "WorkTrail.exe"));
    }

    private static string RepositoryFile(params string[] pathSegments)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "WorkTrail.slnx")))
            {
                return Path.Combine(new[] { directory.FullName }.Concat(pathSegments).ToArray());
            }
        }

        throw new DirectoryNotFoundException("Could not locate the WorkTrail repository root.");
    }
}
