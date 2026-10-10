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


using Microsoft.Data.Sqlite;

namespace WorkTrail.Services;

/// <summary>Owns the activity-store connection contract and applies required PRAGMAs consistently.</summary>
internal sealed class SqliteConnectionFactory
{
    private readonly string _connectionString;

    internal SqliteConnectionFactory(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(databasePath),
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            // Profiling shows a faster open path with pooling, but LocalStore does not yet own a
            // deterministic disposal boundary for every import/reset/test caller. Keep the safe
            // lifecycle contract until pool clearing can be guaranteed before file replacement.
            Pooling = false,
            DefaultTimeout = 5
        }.ToString();
    }

    internal SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        try
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA busy_timeout = 5000; PRAGMA foreign_keys = ON;";
            command.ExecuteNonQuery();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }
}
