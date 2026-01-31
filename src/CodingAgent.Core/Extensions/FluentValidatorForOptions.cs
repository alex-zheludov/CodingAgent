using CodingAgent.Core.Configuration.Validators;

namespace CodingAgent.Core.Extensions;

public static class FluentValidatorForOptions
{
    public static OptionsBuilder<TOptions> ValidateUsingFluentValidator<TOptions>(
        this OptionsBuilder<TOptions> builder, bool validateOnStartup = true)
        where TOptions : class
    {
        builder.Services.AddSingleton<IValidateOptions<TOptions>>(
            serviceProvider => new FluentValidateOptions<TOptions>(
                serviceProvider,
                builder.Name));

        if (validateOnStartup)
        {
            builder.ValidateOnStart();
        }
        
        return builder;
    }
}