using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Utility;
using SharpDX.Direct3D11;

namespace PyonPix.Structs.Browser;

public class Tab : IDisposable {
    public string PixId = string.Empty;
    public bool GpuAcceleration = true;
    public bool SyncCookies = true;
    public TabState State = TabState.Uninitialized;
    public NavigationState NavState = NavigationState.Ready;
    public string? PresentationUri;
    public string? PendingUri;
    public List<NavigationItem> History = [];
    public int CurrentNavigationIndex = -1;
    public NavigationItem? CurrentNavigationItem => CurrentNavigationIndex > -1 && CurrentNavigationIndex < History.Count ? History[CurrentNavigationIndex] : null;

    public IDalamudTextureWrap? FavIcon;

    public nint SharedHandle = nint.Zero;
    public ShaderResourceView? SRV;
    public uint Width;
    public uint Height;
    public Vector2 RenderPos;
    public Vector2 RenderSize;

    public bool CanNavigate => NavState == NavigationState.Ready;
    public bool CanGoBack => State == TabState.Ready && CanNavigate && CurrentNavigationIndex > 0 && History.Count > 0;
    public bool CanGoForward => State == TabState.Ready && CanNavigate && CurrentNavigationIndex != -1 && CurrentNavigationIndex < History.Count - 1;
    public bool CanReload => State == TabState.Ready && CanNavigate && CurrentNavigationItem != null;
    public bool CanCancel => State == TabState.Ready && !CanNavigate && CurrentNavigationItem != null;

    public string GetTitle() {
        if(string.IsNullOrWhiteSpace(CurrentNavigationItem?.Title)) {
            if(string.IsNullOrWhiteSpace(CurrentNavigationItem?.Uri)) {
                return PixId;
            } else {
                var uri = new Uri(CurrentNavigationItem!.Uri);
                var host = uri.Host;
                if(!host.Contains('.')) return host.FirstCharToUpper();
                string[] parts = host.Split('.');
                if(parts.Length <= 2) return parts[1].FirstCharToUpper(); // domain
                if(parts[parts.Length - 1].All(char.IsNumber)) return host; // ipv4
                if(parts[parts.Length - 1].Length == 2 && parts[parts.Length - 2].Length == 2)
                    return parts[1].FirstCharToUpper(); // 2 part tld
                return parts[1].FirstCharToUpper(); // 1 part tld
            }
        } else {
            return CurrentNavigationItem!.Title;
        }
    }

    public string GetHomeUri(HomeUriType type, string homeUri) {
        switch(type) {
            case HomeUriType.Blank:
                return "pix://";
            case HomeUriType.Starry:
                return $"pix://starry";
            default:
                return string.IsNullOrWhiteSpace(homeUri) ? "pix://" : homeUri;
        }
    }

    public void Dispose() {
        FavIcon?.Dispose();
        SRV?.Dispose();
        SharedHandle = nint.Zero;

        GC.SuppressFinalize(this);
    }
}
