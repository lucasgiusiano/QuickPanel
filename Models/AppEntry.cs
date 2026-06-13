namespace QuickPanel.Models;

public class AppEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string Favicon { get; set; } = "";

    public static string FaviconFor(string url)
    {
        try
        {
            var host = new Uri(url).Host;
            return $"https://www.google.com/s2/favicons?sz=64&domain={host}";
        }
        catch { return ""; }
    }
}
