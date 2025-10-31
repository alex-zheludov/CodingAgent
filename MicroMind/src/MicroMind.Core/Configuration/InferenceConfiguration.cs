using System.ComponentModel.DataAnnotations;

namespace MicroMind.Core.Configuration;

public class InferenceConfiguration
{
    [Range(0.0, 2.0)]
    public float Temperature { get; set; } = 0.7f;

    [Range(1, int.MaxValue)]
    public int MaxTokens { get; set; } = 2048;

    [Range(0.0, 1.0)]
    public float TopP { get; set; } = 0.95f;

    [Range(1, int.MaxValue)]
    public int? TopK { get; set; }

    [Range(0.0, 2.0)]
    public float RepetitionPenalty { get; set; } = 1.0f;

    public string Runtime { get; set; } = "LLamaSharp";

    public bool EagerLoading { get; set; } = false;

    [Range(0, int.MaxValue)]
    public int GpuLayers { get; set; } = 0;

    [Range(128, int.MaxValue)]
    public int ContextSize { get; set; } = 4096;
}
