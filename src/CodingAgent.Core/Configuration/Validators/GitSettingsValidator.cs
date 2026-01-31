using FluentValidation;

namespace CodingAgent.Core.Configuration.Validators;

public class GitSettingsValidator : AbstractValidator<GitSettings>
{
    public GitSettingsValidator()
    {
        RuleFor(x => x)
            .Must(x => string.IsNullOrWhiteSpace(x.SshKeyPath) || string.IsNullOrWhiteSpace(x.SshKeyBase64))
            .WithMessage("Cannot specify both Git:SshKeyPath and Git:SshKeyBase64, choose one");
    }
}
