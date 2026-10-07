using System.Text.Json;

namespace WorkFlowWidget.Infrastructure;

public sealed record AzureDevOpsSettings(string Organization, string[] Projects);

public static class WorkSettings
{
    public static AzureDevOpsSettings LoadDevOps()
    {
        var path = File.Exists(AuthenticationSettings.FilePath) ? AuthenticationSettings.FilePath
            : Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (!document.RootElement.TryGetProperty("AzureDevOps", out var section))
            return new AzureDevOpsSettings("", []);
        var settings = section.Deserialize<AzureDevOpsSettings>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return settings is null ? new AzureDevOpsSettings("", []) : settings with { Projects = settings.Projects ?? [] };
    }
}
