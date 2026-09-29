using System.Reflection;
using System.Text.Json;

namespace WifiProfileSwitcher.Setup;

internal sealed record UiSpec(string Title, int Width, int Height, int Padding, int LabelWidth, int RowHeight,
    string Background, string Surface, string Text, string Muted, string Accent, string Hint,
    string AdapterLabel, string SsidLabel, string AddressLabel, string SubnetLabel, string GatewayLabel, string DnsLabel)
{
    public static UiSpec Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ui-spec.json")
            ?? throw new InvalidOperationException("UI resource missing");
        return JsonSerializer.Deserialize<UiSpec>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("UI resource invalid");
    }
}
