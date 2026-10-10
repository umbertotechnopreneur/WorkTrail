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


namespace WorkTrail.Application;

/// <summary>Holds the immutable settings value currently owned by the runtime.</summary>
public sealed class SettingsSnapshot
{
    private AppSettings _value;

    /// <summary>Initializes a settings snapshot with the validated runtime value.</summary>
    public SettingsSnapshot(AppSettings value)
    {
        _value = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>Gets the current immutable settings value without accessing persistence.</summary>
    public AppSettings Value => Volatile.Read(ref _value);

    /// <summary>Replaces the value after the corresponding persistence operation succeeds.</summary>
    public void Replace(AppSettings value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Volatile.Write(ref _value, value);
    }
}
