using FluentValidation;

namespace CodingAgent.Configuration.Validators;

public class AgentSettingsValidator : AbstractValidator<AgentSettings>
{
    public AgentSettingsValidator()
    {
        RuleFor(x => x.Session.SessionId)
            .NotEmpty()
            .WithMessage("Agent:SessionId is required")
            .Must(BeValidGuid)
            .WithMessage("Agent:SessionId must be a valid GUID");

        RuleFor(x => x.Session.Root)
            .NotEmpty()
            .WithMessage("Agent:SessionRoot is required");

        RuleFor(x => x.Repositories)
            .NotEmpty()
            .WithMessage("Agent:Repositories is required and must contain at least one repository");

        RuleForEach(x => x.Repositories)
            .SetValidator(new RepositoryConfigValidator());

        RuleFor(x => x.Workspace)
            .NotNull()
            .SetValidator(new WorkspaceConfigValidator());
    }

    private bool BeValidGuid(string value)
    {
        return Guid.TryParse(value, out _);
    }
}

public class RepositoryConfigValidator : AbstractValidator<RepositoryConfig>
{
    public RepositoryConfigValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("Repository Name is required")
            .Must(BeValidDirectoryName)
            .WithMessage("Repository Name contains invalid characters");

        RuleFor(x => x)
            .Must(HaveEitherUrlOrLocalPath)
            .WithMessage("Repository must have either Url or LocalPath specified, but not both");

        RuleFor(x => x.Url)
            .Must(url => string.IsNullOrEmpty(url) || url.StartsWith("git@") || url.StartsWith("https://"))
            .WithMessage("Repository URL must be SSH format (git@...) or HTTPS format (https://...) when provided");

        RuleFor(x => x.LocalPath)
            .Must(path => string.IsNullOrEmpty(path) || Path.IsPathRooted(path))
            .WithMessage("Repository LocalPath must be an absolute path when provided");

        RuleFor(x => x.Branch)
            .NotEmpty()
            .WithMessage("Repository Branch is required");
    }

    private bool BeValidDirectoryName(string name)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        return !name.Any(c => invalidChars.Contains(c));
    }

    private bool HaveEitherUrlOrLocalPath(RepositoryConfig config)
    {
        var hasUrl = !string.IsNullOrEmpty(config.Url);
        var hasLocalPath = !string.IsNullOrEmpty(config.LocalPath);
        return (hasUrl && !hasLocalPath) || (!hasUrl && hasLocalPath);
    }
}

public class WorkspaceConfigValidator : AbstractValidator<WorkspaceConfig>
{
    public WorkspaceConfigValidator()
    {
        RuleFor(x => x.Root)
            .NotEmpty()
            .WithMessage("Agent:Workspace:Root is required")
            .Must(Path.IsPathRooted)
            .WithMessage("Agent:Workspace:Root must be an absolute path");
    }
}
