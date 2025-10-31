using System.ComponentModel.DataAnnotations;

namespace MicroMind.Core.Configuration;

public class MicroMindOptions
{
    [Required]
    public ModelConfiguration Model { get; set; } = new();

    public InferenceConfiguration Inference { get; set; } = new();

    public DownloadConfiguration Download { get; set; } = new();

    public CacheConfiguration Cache { get; set; } = new();
}
