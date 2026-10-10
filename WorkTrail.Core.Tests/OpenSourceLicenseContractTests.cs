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


using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WorkTrail.Application;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

[CollectionDefinition("Build information manifest", DisableParallelization = true)]
public sealed class BuildInformationManifestCollection
{
}

[Collection("Build information manifest")]
public sealed class OpenSourceLicenseContractTests
{
    private const string CanonicalLicenseName = "MIT License";
    private const string CanonicalLicenseText =
        """
        MIT License

        Copyright (c) 2026 Umberto Giacobbi

        Permission is hereby granted, free of charge, to any person obtaining a copy
        of this software and associated documentation files (the "Software"), to deal
        in the Software without restriction, including without limitation the rights
        to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
        copies of the Software, and to permit persons to whom the Software is
        furnished to do so, subject to the following conditions:

        The above copyright notice and this permission notice shall be included in all
        copies or substantial portions of the Software.

        THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
        IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
        FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
        AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
        LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
        OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
        SOFTWARE.
        """;

    [Fact]
    public async Task ProductInformation_ReportsTheMitLicense()
    {
        var dataDirectory = CreateTemporaryDirectory();
        var buildManifestPath = Path.Combine(AppContext.BaseDirectory, "BuildInfo.json");
        var existingBuildManifest = File.Exists(buildManifestPath)
            ? File.ReadAllBytes(buildManifestPath)
            : null;
        try
        {
            var build = new BuildInformation(
                1,
                "1.0.0-test",
                "1.0.0.0",
                DateTimeOffset.UtcNow,
                DateTimeOffset.Now,
                "test-machine",
                new string('0', 40),
                new string('0', 7),
                false,
                "Debug",
                "x64",
                "win-x64");
            File.WriteAllText(
                buildManifestPath,
                JsonSerializer.Serialize(build, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

            var store = new LocalStore(dataDirectory);
            var utilities = new UtilityService();
            await using var application = new WorkTrailApplication(
                store,
                utilities,
                new TrackingDomainService(store),
                new ScreenCaptureService(utilities.GetAppVersion()),
                new FakeHardwareTelemetryService(),
                new OpenAiAnalysisService(
                    store,
                    new ScreenCaptureService(utilities.GetAppVersion())),
                new StartupService(),
                new BuildInformationService());

            var result = await application.GetProductInformationAsync(CancellationToken.None);

            Assert.True(result.Succeeded);
            Assert.Equal(CanonicalLicenseName, result.Value?.License);
        }
        finally
        {
            if (existingBuildManifest is null)
            {
                File.Delete(buildManifestPath);
            }
            else
            {
                File.WriteAllBytes(buildManifestPath, existingBuildManifest);
            }

            await DeleteTemporaryDirectoryAsync(dataDirectory);
        }
    }

    [Fact]
    public void PublicLicense_DeclaresTheMitContract()
    {
        var license = File.ReadAllText(RepositoryFile("LICENSE"));
        Assert.Equal(
            CanonicalLicenseText.ReplaceLineEndings("\n").TrimEnd('\n'),
            license.ReplaceLineEndings("\n").TrimEnd('\n'));
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

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "WorkTrail.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task DeleteTemporaryDirectoryAsync(string path)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 4)
            {
                await Task.Delay(50);
            }
            catch (UnauthorizedAccessException) when (attempt < 4)
            {
                await Task.Delay(50);
            }
        }
    }
}
