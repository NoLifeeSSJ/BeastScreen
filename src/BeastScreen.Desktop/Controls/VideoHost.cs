using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace BeastScreen.Desktop.Controls;

/// <summary>Native child window: WPF chrome occupies sibling layout cells, never overlays video.</summary>
public sealed class VideoHost : HwndHost
{
    private nint child;
    public event Action<nint>? Ready;
    public event Action<Key>? NativeKey;
    public event Action? DoubleClicked;
    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        child = CreateWindowEx(0, "static", "BeastScreen video", 0x40000000 | 0x10000000 | 0x02000000 | 0x00000100, 0, 0, Math.Max(1, (int)ActualWidth), Math.Max(1, (int)ActualHeight), hwndParent.Handle, 0, 0, 0);
        if (child == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        Ready?.Invoke(child); return new(this, child);
    }
    protected override nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == 0x0100) { NativeKey?.Invoke(KeyInterop.KeyFromVirtualKey((int)wParam)); handled = true; return 0; }
        if (msg == 0x0201) SetFocus(child);
        if (msg == 0x0203) { DoubleClicked?.Invoke(); handled = true; return 0; }
        return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
    }
    protected override void DestroyWindowCore(HandleRef hwnd) { DestroyWindow(hwnd.Handle); child = 0; }
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern nint CreateWindowEx(int exStyle, string className, string windowName, int style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern nint SetFocus(nint hwnd);
}
