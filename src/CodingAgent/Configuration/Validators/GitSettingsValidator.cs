using FluentValidation;

namespace CodingAgent.Configuration.Validators;

public class GitSettingsValidator : AbstractValidator<GitSettings>
{
    public GitSettingsValidator()
    {
        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.SshKeyPath) || !string.IsNullOrWhiteSpace(x.SshKeyBase64))
            .WithMessage("Either Git:SshKeyPath or Git:SshKeyBase64 is required");

        RuleFor(x => x)
            .Must(x => string.IsNullOrWhiteSpace(x.SshKeyPath) || string.IsNullOrWhiteSpace(x.SshKeyBase64))
            .WithMessage("Cannot specify both Git:SshKeyPath and Git:SshKeyBase64, choose one");
    }
}
