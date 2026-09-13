// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ReadyCode.Core.Interop;

/// <summary>
/// Reads the OS-level Caps Lock key state, for the status bar's lock-key indicator, and toggles
/// it on platforms where that's supported. Shared by both front ends so this only needs writing
/// (and re-verifying) once per platform.
/// </summary>
public static class KeyboardLockKeys
{
    #region Public Properties

    /// <summary>
    /// Gets whether Caps Lock is currently toggled on. Always false on Linux - there is no
    /// portable way to read a lock-key indicator across window managers/display servers there,
    /// the same gap as VICE window-raising (see ViceClient).
    /// </summary>
    public static bool IsCapsLockOn =>
        OperatingSystem.IsWindows() ? IsCapsLockOnWindows()
        : OperatingSystem.IsMacOS() ? IsCapsLockOnMacOS()
        : false;

    #endregion

    #region Public Methods

    /// <summary>
    /// Toggles the system Caps Lock state, as if the physical key were pressed. No-op on macOS
    /// and Linux: simulating a real lock-key toggle from user space needs either an Accessibility/
    /// Input-Monitoring-gated synthetic event (macOS) or a display-server-specific API with no
    /// portable equivalent (Linux/X11 vs. Wayland), neither of which is verified here yet.
    /// </summary>
    public static void ToggleCapsLock()
    {
        if (OperatingSystem.IsWindows())
            ToggleCapsLockWindows();
    }

    #endregion

    #region Windows

    // Reads/writes go straight to the Win32 APIs rather than a UI-framework wrapper (e.g. WPF's
    // Keyboard.IsKeyToggled), which has been unreliable at app startup on some machines. Toggling
    // simulates a real key press via keybd_event so the change is a true system-wide lock-key
    // toggle (matching what pressing the physical key would do), not just a visual flag local to
    // one app.
    private const int _vkCapital = 0x14;
    private const uint _keyEventFExtendedKey = 0x1;
    private const uint _keyEventFKeyUp = 0x2;
    private const uint _mapVkVkToVsc = 0;

    // The low-order bit of GetKeyState's return value is the key's toggle state - reliable
    // regardless of window focus/activation timing, unlike a UI framework's own toggle query.
    [SupportedOSPlatform("windows")]
    private static bool IsCapsLockOnWindows() => (GetKeyState(_vkCapital) & 1) != 0;

    [SupportedOSPlatform("windows")]
    private static void ToggleCapsLockWindows()
    {
        byte vk = (byte)_vkCapital;
        byte scanCode = (byte)MapVirtualKey(_vkCapital, _mapVkVkToVsc);
        keybd_event(vk, scanCode, _keyEventFExtendedKey, UIntPtr.Zero);
        keybd_event(vk, scanCode, _keyEventFExtendedKey | _keyEventFKeyUp, UIntPtr.Zero);
    }

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    #endregion

    #region macOS

    // CGEventSourceFlagsState reads the live modifier-lock state straight from the HID system,
    // independent of which app has focus, and (unlike posting a synthetic event) needs no
    // Accessibility/Input Monitoring permission for a plain read. Verified empirically against
    // this Mac's actual Caps Lock state before this was wired into the app.
    private const int _kCGEventSourceStateHIDSystemState = 1;
    private const ulong _kCGEventFlagMaskAlphaShift = 0x10000;

    [SupportedOSPlatform("macos")]
    private static bool IsCapsLockOnMacOS() =>
        (CGEventSourceFlagsState(_kCGEventSourceStateHIDSystemState) & _kCGEventFlagMaskAlphaShift) != 0;

    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    private static extern ulong CGEventSourceFlagsState(int stateID);

    #endregion
}
