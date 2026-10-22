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
using Xunit;

namespace WorkTrail.Core.Tests;

/// <summary>Protects the boundary between public repository workflows and private Store publication.</summary>
public sealed class StorePublishingWorkflowContractTests
{
    #region Implementation

    /// <summary>Keeps Microsoft Store publication out of the checked-in automation workflows.</summary>
    [Fact]
    public void RepositoryWorkflows_DoNotPublishToMicrosoftStore()
    {
        var workflowPaths = Directory.GetFiles(RepositoryFile(".github", "workflows"), "*.yml");

        Assert.NotEmpty(workflowPaths);

        foreach (var workflowPath in workflowPaths)
        {
            var workflow = File.ReadAllText(workflowPath);

            Assert.DoesNotContain("msstore", workflow, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("submission publish", workflow, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("STORE_AUTOPUBLISH", workflow, StringComparison.Ordinal);
            Assert.DoesNotContain("AZURE_AD_APPLICATION_SECRET", workflow, StringComparison.Ordinal);
            Assert.DoesNotContain("--clientSecret", workflow, StringComparison.Ordinal);
        }
    }

    /// <summary>Keeps private Store listing material and its publication workflow outside the repository.</summary>
    [Fact]
    public void PrivateStoreListingAndItsWorkflow_AreNotBundledWithRepository()
    {
        Assert.False(File.Exists(RepositoryFile("store", "listing.json")));
        Assert.False(File.Exists(RepositoryFile(".github", "workflows", "store-listing.yml")));
    }

    /// <summary>Finds a checked-in fixture by walking upward from the test output to the repository root.</summary>
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

    #endregion
}
