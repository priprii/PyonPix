using System;
using System.Linq;
using Dalamud.Bindings.ImGui;

using PyonPix.Interop;
using PyonPix.Structs.Browser;

namespace PyonPix.Utility;

public static class BrowserUtil {
    private static readonly string[] InternalSchemes = ["pix://", "file:///", "about:", "edge://", "extension://", "chrome://", "chrome-extension://"];

    public static string NormalizeUri(string? uri) {
        if(string.IsNullOrWhiteSpace(uri) || uri == "about:blank") return "pix://";

        uri = uri.Trim();

        if(InternalSchemes.Any(x => uri.StartsWith(x)))
            return uri;

        if(Uri.TryCreate(uri, UriKind.Absolute, out var abs) && IsNavigableHost(abs))
            return abs.ToString();
        if(Uri.TryCreate($"https://{uri}", UriKind.Absolute, out abs) && IsNavigableHost(abs))
            return abs.ToString();

        return $"https://google.com/search?q={Uri.EscapeDataString(uri)}";
    }

    private static bool IsNavigableHost(Uri uri) {
        return uri.HostNameType switch {
            UriHostNameType.Dns => uri.Host.Contains('.') && !uri.Host.EndsWith('.'),
            UriHostNameType.IPv4 => true,
            UriHostNameType.IPv6 => true,
            _ => false
        };
    }

    public static string FormatUriForDisplay(string uri) {
        if(string.IsNullOrEmpty(uri)) return uri;

        if(!Uri.TryCreate(uri, UriKind.Absolute, out var absolute))
            return uri;

        var host = absolute.Host;
        var path = absolute.AbsolutePath;

        return string.IsNullOrWhiteSpace(path) || path == "/" ? string.IsNullOrWhiteSpace(host) ? uri : host : $"{host}{path}";
    }

    public static ImGuiMouseCursor TranslateCursor(uint id) => id switch {
        (uint)Win32Interop.IDC_STANDARD_CURSORS.IDC_ARROW => ImGuiMouseCursor.Arrow,
        (uint)Win32Interop.IDC_STANDARD_CURSORS.IDC_IBEAM => ImGuiMouseCursor.TextInput,
        (uint)Win32Interop.IDC_STANDARD_CURSORS.IDC_HAND => ImGuiMouseCursor.Hand,
        (uint)Win32Interop.IDC_STANDARD_CURSORS.IDC_SIZEWE => ImGuiMouseCursor.ResizeEw,
        (uint)Win32Interop.IDC_STANDARD_CURSORS.IDC_SIZENS => ImGuiMouseCursor.ResizeNs,
        (uint)Win32Interop.IDC_STANDARD_CURSORS.IDC_SIZENWSE => ImGuiMouseCursor.ResizeNwse,
        (uint)Win32Interop.IDC_STANDARD_CURSORS.IDC_SIZENESW => ImGuiMouseCursor.ResizeNesw,
        (uint)Win32Interop.IDC_STANDARD_CURSORS.IDC_SIZEALL => ImGuiMouseCursor.ResizeAll,
        (uint)Win32Interop.IDC_STANDARD_CURSORS.IDC_WAIT => ImGuiMouseCursor.NotAllowed,
        0 => ImGuiMouseCursor.ResizeAll, // Wheel Click/Grab, maybe ignore
        _ => ImGuiMouseCursor.Arrow
    };

    public static MouseButton GetMouseButtonsState(Span<bool> state) {
        var buttons = MouseButton.None;
        if(state[0]) buttons |= MouseButton.Left;
        if(state[1]) buttons |= MouseButton.Right;
        if(state[2]) buttons |= MouseButton.Middle;
        return buttons;
    }
}
