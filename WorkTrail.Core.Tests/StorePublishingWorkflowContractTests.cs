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

public sealed class StorePublishingWorkflowContractTests
{
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

    [Fact]
    public void PrivateStoreListingAndItsWorkflow_AreNotBundledWithRepository()
    {
        Assert.False(File.Exists(RepositoryFile("store", "listing.json")));
        Assert.False(File.Exists(RepositoryFile(".github", "workflows", "store-listing.yml")));
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
