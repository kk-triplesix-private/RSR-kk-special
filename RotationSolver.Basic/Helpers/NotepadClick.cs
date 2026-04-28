using System.Runtime.InteropServices;

namespace RotationSolver.Basic.Helpers;

internal static class NotepadClick
{
    private const uint WM_CHAR = 0x0102;

    private static IntPtr _cachedTarget = IntPtr.Zero;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindowEx(IntPtr hWndParent, IntPtr hWndChildAfter, string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, nuint wParam, nint lParam);

    public static void Click()
    {
        if (_cachedTarget == IntPtr.Zero || !IsWindow(_cachedTarget))
        {
            _cachedTarget = ResolveTarget();
        }

        if (_cachedTarget == IntPtr.Zero)
        {
            return;
        }

        _ = PostMessage(_cachedTarget, WM_CHAR, '-', 0);
    }

    private static IntPtr ResolveTarget()
    {
        IntPtr npp = FindWindow("Notepad++", null);
        if (npp != IntPtr.Zero)
        {
            IntPtr scintilla = FindWindowEx(npp, IntPtr.Zero, "Scintilla", null);
            return scintilla != IntPtr.Zero ? scintilla : npp;
        }

        IntPtr classic = FindWindow("Notepad", null);
        if (classic != IntPtr.Zero)
        {
            IntPtr edit = FindWindowEx(classic, IntPtr.Zero, "Edit", null);
            if (edit == IntPtr.Zero)
            {
                edit = FindWindowEx(classic, IntPtr.Zero, "RichEditD2DPT", null);
            }
            return edit != IntPtr.Zero ? edit : classic;
        }

        return IntPtr.Zero;
    }
}
