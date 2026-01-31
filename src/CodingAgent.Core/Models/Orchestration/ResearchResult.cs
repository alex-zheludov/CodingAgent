namespace CodingAgent.Core.Models.Orchestration;

public class ResearchResult
{
    public string Answer { get; set; } = string.Empty;
    public List<CodeReference> References { get; set; } = new();
    public double Confidence { get; set; }
}

public class CodeReference
{
    public string File { get; set; } = string.Empty;
    public int LineStart { get; set; }
    public int LineEnd { get; set; }
    public string Snippet { get; set; } = string.Empty;
}
