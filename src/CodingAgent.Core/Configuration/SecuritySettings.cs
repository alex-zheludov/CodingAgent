namespace CodingAgent.Core.Configuration;

public class SecuritySettings
{
    public const string SectionName = "Security";

    // TODO: This should be determined somewat dynamically
    
    public List<string> AllowedCommands { get; set; } = new()
    {
        "dotnet",
        "npm"
    };

    public int FileSizeLimitMB { get; set; } = 10;

    public long FileSizeLimitBytes => FileSizeLimitMB * 1024L * 1024L;
}
