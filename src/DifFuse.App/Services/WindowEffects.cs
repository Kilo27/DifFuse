using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace DifFuse.App.Services;

/// <summary>Dark title bar and, on Windows 11, the Mica backdrop.</summary>
internal static class WindowEffects
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaSystemBackdropType = 38;
    private const int DwmsbtMainWindow = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left, Right, Top, Bottom;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    public static void UseDarkTitleBar(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        int on = 1;
        DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref on, sizeof(int));
    }

    /// <summary>Turns on Mica behind the window. Returns false (and changes nothing) where it is unsupported.</summary>
    public static bool TryUseMica(Window window)
    {
        if (Environment.OSVersion.Version.Build < 22621) return false;
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        int backdrop = DwmsbtMainWindow;
        if (DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref backdrop, sizeof(int)) != 0) return false;

        if (HwndSource.FromHwnd(hwnd)?.CompositionTarget is { } target)
        {
            target.BackgroundColor = Colors.Transparent;
        }
        var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(hwnd, ref margins);
        window.Background = Brushes.Transparent;
        return true;
    }
}
