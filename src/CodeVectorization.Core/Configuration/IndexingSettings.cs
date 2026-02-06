namespace CodeVectorization.Core.Configuration;

public class IndexingSettings
{
    public const string SectionName = "Indexing";

    public int ChunkSize { get; set; } = 512;
    public int BatchSize { get; set; } = 100;
}
