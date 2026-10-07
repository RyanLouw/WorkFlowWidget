using System.Text.Json;

namespace WorkFlowWidget.Infrastructure;

public sealed record AuthenticationSettings(string ClientId, string TenantId)
{
    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WorkFlowWidget", "appsettings.json");

    public static AuthenticationSettings Load()
    {
        var path = File.Exists(FilePath) ? FilePath : Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (!File.Exists(path))
            throw new InvalidOperationException($"Configure your Entra app first. Create {FilePath} using the example in the README.");

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (!document.RootElement.TryGetProperty("Authentication", out var section))
            throw new InvalidOperationException("appsettings.json must contain an Authentication section.");
        var settings = section.Deserialize<AuthenticationSettings>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (settings is null || !Guid.TryParse(settings.ClientId, out _) || !Guid.TryParse(settings.TenantId, out _))
            throw new InvalidOperationException("Authentication configuration must contain valid ClientId and TenantId GUIDs.");
        return settings;
    }
}
