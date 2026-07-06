using PyonPix.Utility;

namespace PyonPix.Structs.Browser;

public class NavigationItem(string uri) {
    public string Uri = uri;
    public string Title = string.Empty;

    public string GetDisplayTitle() {
        return string.IsNullOrWhiteSpace(Title) ? BrowserUtil.FormatUriForDisplay(Uri) : Title;
    }
}
