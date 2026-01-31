using FluentValidation;

namespace CodingAgent.Core.Configuration.Validators;

public class AzureOpenAISettingsValidator : AbstractValidator<AzureOpenAISettings>
{
    public AzureOpenAISettingsValidator()
    {
        RuleFor(x => x.Endpoint)
            .NotEmpty()
            .WithMessage("AzureOpenAI:Endpoint is required")
            .Must(uri => Uri.TryCreate(uri, UriKind.Absolute, out _))
            .WithMessage("AzureOpenAI:Endpoint must be a valid URL");

        RuleFor(x => x.ApiKey)
            .NotEmpty()
            .WithMessage("AzureOpenAI:ApiKey is required");

        RuleFor(x => x.Model)
            .NotEmpty()
            .WithMessage("AzureOpenAI:Model is required");

        RuleFor(x => x.MaxTokens)
            .GreaterThan(0)
            .WithMessage("AzureOpenAI:MaxTokens must be greater than 0");

        RuleFor(x => x.Temperature)
            .InclusiveBetween(0m, 2m)
            .WithMessage("AzureOpenAI:Temperature must be between 0 and 2");
    }
}
