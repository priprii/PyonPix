using System;
using System.Runtime.InteropServices;
using static PyonPix.Interop.Win32Interop;

namespace PyonPix.Interop;

public sealed class WindowSubclass : IDisposable {
    private readonly nint Hwnd;
    private readonly WndProcDelegate Callback;
    private readonly nint CallbackPtr;
    private nint CacheWndProc;
    private bool IsDisposed;

    public delegate long WndProcDelegate(nint hWnd, uint msg, ulong wParam, long lParam);

    [DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
    private static extern long CallWindowProc(nint lpPrevWndFunc, nint hWnd, uint msg, ulong wParam, long lParam);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint hWnd, WindowLongFlags nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint hWnd, WindowLongFlags nIndex, nint dwNewLong);

    public WindowSubclass(nint hWnd, WndProcDelegate callback) {
        Hwnd = hWnd;
        Callback = callback;

        CallbackPtr = Marshal.GetFunctionPointerForDelegate(Callback);
        CacheWndProc = SetWindowLongPtr(Hwnd, WindowLongFlags.GWL_WNDPROC, CallbackPtr);
    }

    public long CallOriginal(nint hWnd, uint msg, ulong wParam, long lParam) => CallWindowProc(CacheWndProc, hWnd, msg, wParam, lParam);

    public void Dispose() {
        if(IsDisposed) return;
        IsDisposed = true;

        var current = GetWindowLongPtr(Hwnd, WindowLongFlags.GWL_WNDPROC);
        if(current == CallbackPtr && CacheWndProc != nint.Zero) {
            SetWindowLongPtr(Hwnd, WindowLongFlags.GWL_WNDPROC, CacheWndProc);
        }
        CacheWndProc = nint.Zero;
        GC.SuppressFinalize(this);
    }

    ~WindowSubclass() {
        Dispose();
    }
}
