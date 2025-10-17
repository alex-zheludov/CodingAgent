namespace CodingAgent.Configuration;

public class SecuritySettings
{
    public const string SectionName = "Security";

    public List<string> AllowedCommands { get; set; } = new()
    {
        "dotnet build",
        "dotnet test",
        "dotnet restore",
        "npm install",
        "npm test",
        "npm run"
    };

    public int FileSizeLimitMB { get; set; } = 10;

    public long FileSizeLimitBytes => FileSizeLimitMB * 1024L * 1024L;
}
