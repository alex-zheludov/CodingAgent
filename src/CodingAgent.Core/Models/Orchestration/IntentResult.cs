namespace CodingAgent.Models.Orchestration;

public class IntentResult
{
    public IntentType Intent { get; set; }
    public double Confidence { get; set; }
    public string Reasoning { get; set; } = string.Empty;
}

public enum IntentType
{
    Question,
    Task,
    Greeting,
    Unclear
}
