namespace CodeVectorization.Core.Configuration;

public class QdrantSettings
{
    public const string SectionName = "Qdrant";

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 6334;
}
