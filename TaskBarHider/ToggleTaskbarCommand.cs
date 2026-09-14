using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Microsoft.Win32;

namespace TaskBarHider;

internal sealed partial class ToggleTaskbarCommand : InvokableCommand
{
    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;
    private const int ABM_SETSTATE = 0x0000000A;
    private const int ABS_MANUAL = 0x0000000;
    private const int ABS_AUTOHIDE = 0x0000001;

    // Tracks state across invocations for the lifetime of the extension host.
    private static bool _isHidden;

    // Keeps forcing the taskbar hidden even when Windows would otherwise
    // reveal it on mouse hover (matches the original app's behavior).
    private static System.Threading.Timer? _forceHideTimer;

    public ToggleTaskbarCommand()
    {
        Name = "Toggle Taskbar";
        Icon = new IconInfo("\uE7C4"); // generic system icon glyph, swap for your own later
    }

    public override CommandResult Invoke()
    {
        _isHidden = !_isHidden;

        if (_isHidden)
        {
            HideAllTaskbars();
            _forceHideTimer = new System.Threading.Timer(
                _ => ForceHideTaskbarWindows(),
                null,
                dueTime: 30,
                period: 30);
        }
        else
        {
            _forceHideTimer?.Dispose();
            _forceHideTimer = null;
            RestoreAllTaskbars();
        }

        // Closes the palette after running. Use CommandResult.KeepOpen() instead
        // if you'd rather the list stay open after toggling.
        return CommandResult.Dismiss();
    }

    // Re-hides anything Windows tried to sneak back into view. Runs on the
    // timer while hidden is active.
    private static void ForceHideTaskbarWindows()
    {
        IntPtr mainTaskbar = FindWindow("Shell_TrayWnd", null);
        IntPtr overflow = FindWindow("NotifyIconOverflowWindow", null);
        List<IntPtr> secondaryTaskbars = GetSecondaryTaskbars();

        if (mainTaskbar != IntPtr.Zero && IsWindowVisible(mainTaskbar))
        {
            ShowWindow(mainTaskbar, SW_HIDE);
        }
        foreach (IntPtr hwnd in secondaryTaskbars)
        {
            if (IsWindowVisible(hwnd))
            {
                ShowWindow(hwnd, SW_HIDE);
            }
        }
        if (overflow != IntPtr.Zero && IsWindowVisible(overflow))
        {
            ShowWindow(overflow, SW_HIDE);
        }
    }

    // ---------------------------------------------------------------
    // Core logic (ported from the original C++ WinMain-based version)
    // ---------------------------------------------------------------

    private static void HideAllTaskbars()
    {
        IntPtr mainTaskbar = FindWindow("Shell_TrayWnd", null);
        IntPtr overflow = FindWindow("NotifyIconOverflowWindow", null);
        List<IntPtr> secondaryTaskbars = GetSecondaryTaskbars();

        if (mainTaskbar == IntPtr.Zero)
        {
            return;
        }

        SetAppBarState(mainTaskbar, ABS_AUTOHIDE);
        foreach (IntPtr hwnd in secondaryTaskbars)
        {
            SetAppBarState(hwnd, ABS_AUTOHIDE);
        }
        UpdateRegistryAutoHide(true);

        ShowWindow(mainTaskbar, SW_HIDE);
        foreach (IntPtr hwnd in secondaryTaskbars)
        {
            ShowWindow(hwnd, SW_HIDE);
        }
        if (overflow != IntPtr.Zero)
        {
            ShowWindow(overflow, SW_HIDE);
        }
    }

    private static void RestoreAllTaskbars()
    {
        IntPtr mainTaskbar = FindWindow("Shell_TrayWnd", null);
        IntPtr overflow = FindWindow("NotifyIconOverflowWindow", null);
        List<IntPtr> secondaryTaskbars = GetSecondaryTaskbars();

        ShowWindow(mainTaskbar, SW_SHOW);
        foreach (IntPtr hwnd in secondaryTaskbars)
        {
            ShowWindow(hwnd, SW_SHOW);
        }
        if (overflow != IntPtr.Zero)
        {
            ShowWindow(overflow, SW_SHOW);
        }

        SetAppBarState(mainTaskbar, ABS_MANUAL);
        foreach (IntPtr hwnd in secondaryTaskbars)
        {
            SetAppBarState(hwnd, ABS_MANUAL);
        }
        UpdateRegistryAutoHide(false);
    }

    private static List<IntPtr> GetSecondaryTaskbars()
    {
        var results = new List<IntPtr>();

        EnumWindows((hwnd, _) =>
        {
            var sb = new StringBuilder(256);
            GetClassName(hwnd, sb, sb.Capacity);
            string className = sb.ToString();

            if (className == "Shell_SecondaryTrayWnd" || className == "SecondaryTrayWnd")
            {
                results.Add(hwnd);
            }

            return true; // keep enumerating
        }, IntPtr.Zero);

        return results;
    }

    private static void SetAppBarState(IntPtr hWnd, int state)
    {
        if (hWnd == IntPtr.Zero)
        {
            return;
        }

        var data = new APPBARDATA
        {
            cbSize = Marshal.SizeOf(typeof(APPBARDATA)),
            hWnd = hWnd,
            lParam = (IntPtr)state,
        };
        SHAppBarMessage(ABM_SETSTATE, ref data);
    }

    private static void UpdateRegistryAutoHide(bool autoHide)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\StuckRects3", writable: true);

        if (key?.GetValue("Settings") is byte[] data && data.Length > 8)
        {
            data[8] = autoHide ? (byte)0x03 : (byte)0x02;
            key.SetValue("Settings", data, RegistryValueKind.Binary);
        }
    }

    // ---------------------------------------------------------------
    // Win32 interop
    // ---------------------------------------------------------------

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public IntPtr lParam;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("shell32.dll")]
    private static extern IntPtr SHAppBarMessage(int dwMessage, ref APPBARDATA pData);
}