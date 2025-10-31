using System.ComponentModel.DataAnnotations;

namespace MicroMind.Core.Configuration;

public class ModelConfiguration
{
    [Required]
    public string Name { get; set; } = "Phi-3-mini-4k-instruct";

    [Required]
    [Url]
    public string SourceUrl { get; set; } = "https://huggingface.co/microsoft/Phi-3-mini-4k-instruct-gguf/resolve/main/Phi-3-mini-4k-instruct-q4.gguf";

    public string? Checksum { get; set; }

    public string ChecksumAlgorithm { get; set; } = "SHA256";

    public string Version { get; set; } = "1.0.0";
}
