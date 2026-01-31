using FluentValidation;

namespace CodingAgent.Core.Configuration.Validators;

// Credit: https://www.milanjovanovic.tech/blog/options-pattern-validation-in-aspnetcore-with-fluentvalidation
public class FluentValidateOptions<TOptions>(IServiceProvider serviceProvider, string? name)
    : IValidateOptions<TOptions>
    where TOptions : class
{
    public ValidateOptionsResult Validate(string? name1, TOptions options)
    {
        if (name is not null && name != name1)
        {
            return ValidateOptionsResult.Skip;
        }

        ArgumentNullException.ThrowIfNull(options);

        using var scope = serviceProvider.CreateScope();

        var validator = scope.ServiceProvider.GetService<IValidator<TOptions>>();

        if (validator is null)
        {
            return ValidateOptionsResult.Skip;
        }
        
        var result = validator.Validate(options);
        if (result.IsValid)
        {
            return ValidateOptionsResult.Success;
        }

        var type = options.GetType().Name;
        var errors = new List<string>();

        foreach (var failure in result.Errors)
        {
            errors.Add($"Validation failed for {type}.{failure.PropertyName} " +
                       $"with the error: {failure.ErrorMessage}");
        }

        return ValidateOptionsResult.Fail(errors);
    }
}