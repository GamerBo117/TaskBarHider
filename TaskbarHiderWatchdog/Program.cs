using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace TaskbarHiderWatchdog;

internal static class Program
{
    private const int SW_SHOW = 5;
    private const int ABM_SETSTATE = 0x0000000A;
    private const int ABS_MANUAL = 0x0000000;

    private static readonly string LogPath = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "taskbarhider_watchdog.log");

    private static void Log(string message)
    {
        try
        {
            System.IO.File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never crash the watchdog.
        }
    }

    private static void Main(string[] args)
    {
        Log($"Started. args=[{string.Join(",", args)}]");

        if (args.Length == 0 || !int.TryParse(args[0], out int parentPid))
        {
            Log("No valid PID argument -- exiting immediately.");
            return;
        }

        Log($"Watching parent PID {parentPid}.");

        try
        {
            using Process parent = Process.GetProcessById(parentPid);
            Log($"Attached to parent process '{parent.ProcessName}'. Waiting for it to exit...");
            parent.WaitForExit(); // blocks with ~0% CPU until the parent process ends
            Log("Parent process exited.");
        }
        catch (Exception ex)
        {
            Log($"Exception while attaching/waiting: {ex.GetType().Name}: {ex.Message}");
        }

        Log("Restoring taskbars now.");
        RestoreAllTaskbars();
        Log("Done, exiting.");
    }

    // ---------------------------------------------------------------
    // Restore logic (mirrors ToggleTaskbarCommand.RestoreAllTaskbars)
    // ---------------------------------------------------------------

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
            return true;
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
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("shell32.dll")]
    private static extern IntPtr SHAppBarMessage(int dwMessage, ref APPBARDATA pData);
}