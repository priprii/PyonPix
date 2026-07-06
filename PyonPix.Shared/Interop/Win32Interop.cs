using System.Runtime.InteropServices;

namespace PyonPix.Shared.Interop;

public static class Win32Interop {
    [DllImport("user32.dll")]
    public static extern sbyte GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);
    [DllImport("user32.dll")]
    public static extern bool TranslateMessage(ref MSG lpMsg);
    [DllImport("user32.dll")]
    public static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct MSG {
        public IntPtr hwnd;
        public UInt32 message;
        public UIntPtr wParam;
        public UIntPtr lParam;
        public UInt32 time;
        public POINT pt;
    }

    public struct POINT {
        public Int32 x;
        public Int32 Y;
    }

    public static void MessageLoop() {
        MSG msg;
        while(GetMessage(out msg, IntPtr.Zero, 0, 0) > 0) {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
    }
}
