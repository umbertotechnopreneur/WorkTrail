// SPDX-License-Identifier: MIT
using System;
using System.IO;
using Microsoft.Data.Sqlite;
using WorkTrail.Application;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

public sealed class ScreenshotImageMetadataTests
{
    [Fact]
    public void Destinations_SurviveReloadAndSeparateAnalysisFromGallery()
    {
        var directory = Path.Combine(Path.GetTempPath(), "metadata-destinations-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new LocalStore(directory);
            var settings = store.LoadSettings() with { ScreenshotDirectory = directory, IncludeDeviceLocation = false };
            store.SaveSettings(settings);
            var capturedAt = DateTimeOffset.Now;
            var day = ScreenshotStorageLayout.GetDayDirectory(directory, capturedAt);
            Directory.CreateDirectory(day);
            var captureId = Guid.NewGuid().ToString("N");
            var both = Image(day, captureId, "active-window", 2, "active-window") with
            {
                Priority = "primary",
                ForAnalysis = true,
                ForGallery = true
            };
            var galleryOnly = Image(day, captureId, "monitor-1", 1, "monitor-context") with
            {
                Priority = "context",
                ForAnalysis = false,
                ForGallery = true
            };
            var analysisOnly = Image(day, captureId, "secondary-window-1", 2, "secondary-window") with
            {
                Priority = "secondary",
                ForAnalysis = true,
                ForGallery = false
            };
            var images = new[] { galleryOnly, analysisOnly, both };
            foreach (var image in images) File.WriteAllBytes(image.Path, [1, 2, 3]);
            store.RegisterScreenshotCapture(captureId, settings.InstallationId, capturedAt, "manual", images);

            var reloaded = new LocalStore(directory);
            var selected = reloaded.SelectAnalysisScreenshotPaths([galleryOnly.Path, analysisOnly.Path, both.Path]);
            Assert.Equal(new[] { analysisOnly.Path, both.Path }, selected);
            Assert.Empty(reloaded.SelectAnalysisScreenshotPaths([galleryOnly.Path]));
            var gallery = reloaded.GetScreenshotGallery(DateOnly.FromDateTime(capturedAt.LocalDateTime));
            Assert.Equal(2, gallery.Items.Count);
            Assert.Contains(gallery.Items, item => item.Path == both.Path);
            Assert.Contains(gallery.Items, item => item.Path == galleryOnly.Path);
            Assert.DoesNotContain(gallery.Items, item => item.Path == analysisOnly.Path);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RegisterAndLoad_PreservesCropIdentityAndRebindsRequestedOrder()
    {
        WithStore((store, directory, installationId) =>
        {
            var captureId = Guid.NewGuid().ToString("N");
            var crop = Image(directory, captureId, "active-window", 2, "active-window");
            var overview = Image(directory, captureId, "monitor-1", 1, "monitor-context");
            store.RegisterScreenshotCapture(captureId, installationId, DateTimeOffset.UtcNow, "manual", [crop, overview]);
            var movedCrop = Path.Combine(directory, "relocated", Path.GetFileName(crop.Path));
            var loaded = store.LoadScreenshotImages([overview.Path, movedCrop]);

            Assert.Equal(2, loaded.Count);
            Assert.Equal(1, loaded[0].ScreenIndex);
            Assert.Equal(2, loaded[1].ScreenIndex);
            Assert.Equal(movedCrop, loaded[1].Path);
            Assert.Equal("active-window", loaded[1].Role);
            Assert.Equal("Test conversation", loaded[1].WindowTitle);
            Assert.Equal(new[] { 1, 2 }, loaded[1].RelatedScreenIndices);
            Assert.Equal(-20, loaded[1].ImageLeft);
            Assert.True(loaded[1].IsActiveScreen);
        });
    }

    [Fact]
    public void Register_StoresPortableIdentityWithoutAbsolutePath()
    {
        WithStore((store, directory, installationId) =>
        {
            var captureId = Guid.NewGuid().ToString("N");
            var image = Image(directory, captureId, "active-window", 2, "active-window");
            store.RegisterScreenshotCapture(captureId, installationId, DateTimeOffset.UtcNow, "manual", [image]);
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = store.DatabasePath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT artifact_identity, metadata_json FROM screenshot_images;";
            using var reader = command.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal(LocalStore.ScreenshotIdentity(Path.GetFileName(image.Path)), reader.GetString(0));
            Assert.DoesNotContain("metadata-tests-", reader.GetString(1));
            Assert.False(reader.Read());
        });
    }

    [Fact]
    public void RegisterAndReload_PreservesReadingHintIdsAndSelectionReason()
    {
        WithStore((store, directory, installationId) =>
        {
            var captureId = Guid.NewGuid().ToString("N");
            var query = Image(directory, captureId, "active-window", 2, "active-window") with
            {
                ApplicationName = "SQL Server Management Studio",
                WindowTitle = "SQLQuery1.sql",
                Attention = "high",
                RuleReason = "title-hint:sql-query",
                MicroPromptIds = ["sql"]
            };
            var debugger = Image(directory, captureId, "secondary-window-1", 1, "secondary-window") with
            {
                ApplicationName = "Visual Studio",
                WindowTitle = "Project (Debugging)",
                Attention = "high",
                RuleReason = "title-hint:ide-debugging",
                MicroPromptIds = ["code", "debugger"]
            };
            store.RegisterScreenshotCapture(captureId, installationId, DateTimeOffset.UtcNow, "manual", [query, debugger]);

            var reloaded = new SqliteActivityStore(Path.Combine(directory, SqliteActivityStore.DatabaseFileName));
            var loaded = reloaded.LoadScreenshotImages([debugger.Path, query.Path]);

            Assert.Equal(2, loaded.Count);
            Assert.Equal("title-hint:ide-debugging", loaded[0].RuleReason);
            Assert.Equal(new[] { "code", "debugger" }, loaded[0].MicroPromptIds);
            Assert.Equal("title-hint:sql-query", loaded[1].RuleReason);
            Assert.Equal(new[] { "sql" }, loaded[1].MicroPromptIds);
            Assert.Equal("high", loaded[0].Attention);
            Assert.Equal("high", loaded[1].Attention);
            var prompt = AiPromptCatalog.RenderScreenshotAnalysis("balanced", null, _ => "{{IMAGE_MANIFEST}}",
                images: loaded, imagePaths: [debugger.Path, query.Path]);
            Assert.Contains("Image 1 (debugger):", prompt);
            Assert.Contains("Image 2 (sql):", prompt);
            Assert.Contains("selection_hint=title-hint:ide-debugging", prompt);
            Assert.Contains("selection_hint=title-hint:sql-query", prompt);
        });
    }

    [Fact]
    public void DeleteCapture_CascadesAllImageMetadata()
    {
        WithStore((store, directory, installationId) =>
        {
            var captureId = Guid.NewGuid().ToString("N");
            var first = Image(directory, captureId, "active-window", 2, "active-window");
            var second = Image(directory, captureId, "monitor-1", 1, "monitor-context");
            store.RegisterScreenshotCapture(captureId, installationId, DateTimeOffset.UtcNow, "manual", [first, second]);

            Assert.Equal(1, store.DeleteOrphanedScreenshotCapture(captureId));
            Assert.Empty(store.LoadScreenshotImages([first.Path, second.Path]));
        });
    }

    [Fact]
    public void DeleteImage_RemovesOnlySelectedMetadataAndPreservesCapture()
    {
        WithStore((store, directory, installationId) =>
        {
            var captureId = Guid.NewGuid().ToString("N");
            var first = Image(directory, captureId, "active-window", 2, "active-window");
            var second = Image(directory, captureId, "monitor-1", 1, "monitor-context");
            store.RegisterScreenshotCapture(captureId, installationId, DateTimeOffset.UtcNow, "manual", [first, second]);

            store.DeleteScreenshotImage(LocalStore.ScreenshotIdentity(Path.GetFileName(first.Path)));

            Assert.Equal(second.Path, Assert.Single(store.LoadScreenshotImages([first.Path, second.Path])).Path);
            Assert.Single(store.LoadScreenshotCaptures([captureId]));
        });
    }

    [Fact]
    public void Register_RejectsImageFromDifferentCaptureAndRollsBackProvenance()
    {
        WithStore((store, directory, installationId) =>
        {
            var captureId = Guid.NewGuid().ToString("N");
            var image = Image(directory, Guid.NewGuid().ToString("N"), "active-window", 2, "active-window");

            Assert.Throws<InvalidDataException>(() => store.RegisterScreenshotCapture(captureId, installationId,
                DateTimeOffset.UtcNow, "manual", [image]));
            Assert.Empty(store.LoadScreenshotCaptures([captureId]));
            Assert.Empty(store.LoadScreenshotImages([image.Path]));
        });
    }

    private static ScreenshotImageMetadata Image(string directory, string captureId, string stem, int screen, string role) =>
        new(Path.Combine(directory, $"{captureId}_1.0.0_manual_{stem}.webp"), role, screen, $"Monitor {screen}",
            0, 0, 1920, 1080, -20, 10, 1000, 800, screen == 2,
            role == "active-window" ? "ChatGPT" : string.Empty,
            role == "active-window" ? "Test conversation" : string.Empty,
            role == "active-window" ? [1, 2] : [screen]);

    private static void WithStore(Action<SqliteActivityStore, string, string> action)
    {
        var directory = Path.Combine(Path.GetTempPath(), "metadata-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new SqliteActivityStore(Path.Combine(directory, SqliteActivityStore.DatabaseFileName));
            var installationId = Guid.NewGuid().ToString("N");
            store.EnsureCurrentInstallationProfile(InstallationProfileCatalog.CreateDefault(installationId, "TEST", DateTimeOffset.UtcNow));
            action(store, directory, installationId);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
