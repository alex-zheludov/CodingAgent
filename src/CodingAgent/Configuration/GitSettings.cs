namespace CodingAgent.Configuration;

public class GitSettings
{
    public const string SectionName = "Git";

    public string? SshKeyPath { get; set; }
    public string? SshKeyBase64 { get; set; }
    public string TargetBranchPrefix { get; set; } = "agent/";
    public string CommitAuthorName { get; set; } = "Code Agent";
    public string CommitAuthorEmail { get; set; } = "agent@codeagent.local";
}
