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
using System.Diagnostics;
using System.IO;
using Microsoft.Data.Sqlite;
using Xunit;
using Xunit.Abstractions;

namespace WorkTrail.Core.Tests;

public sealed class SqliteConnectionProfilingTests(ITestOutputHelper output)
{
    [Fact]
    public void ConnectionOpenProfile_RecordsPoolingDecisionEvidence()
    {
        const int iterations = 500;
        var directory = Path.Combine(Path.GetTempPath(), "WorkTrail.SqliteProfile." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "profile.sqlite3");
        try
        {
            var unpooled = ConnectionString(databasePath, pooling: false);
            var pooled = ConnectionString(databasePath, pooling: true);
            Execute(unpooled, 10);
            var unpooledElapsed = Execute(unpooled, iterations);
            Execute(pooled, 10);
            var pooledElapsed = Execute(pooled, iterations);

            output.WriteLine(
                "SQLite open/select/dispose profile: {0} iterations, Pooling=false {1:F2} ms, Pooling=true {2:F2} ms.",
                iterations,
                unpooledElapsed.TotalMilliseconds,
                pooledElapsed.TotalMilliseconds);
            Assert.True(unpooledElapsed > TimeSpan.Zero);
            Assert.True(pooledElapsed > TimeSpan.Zero);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string ConnectionString(string databasePath, bool pooling) =>
        new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = pooling
        }.ToString();

    private static TimeSpan Execute(string connectionString, int iterations)
    {
        var stopwatch = Stopwatch.StartNew();
        for (var index = 0; index < iterations; index++)
        {
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1;";
            Assert.Equal(1L, Convert.ToInt64(command.ExecuteScalar()));
        }

        stopwatch.Stop();
        return stopwatch.Elapsed;
    }
}
