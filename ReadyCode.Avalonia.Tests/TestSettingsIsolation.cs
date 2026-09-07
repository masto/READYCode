// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.CompilerServices;

namespace ReadyCode.Avalonia.Tests;

/// <summary>
/// Points AppSettings at a throwaway folder for the whole test run, so tests never read or
/// overwrite the developer's real READYCode settings.
/// </summary>
internal static class TestSettingsIsolation
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"readycode-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        Environment.SetEnvironmentVariable("READYCODE_SETTINGS_DIR", dir);
    }
}
