using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Orvano.Modules.Auth.Contracts;
using Orvano.Modules.Auth.Passwords;

namespace Orvano.Modules.Auth;

public static class AuthModule
{
    public static IServiceCollection AddAuthModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<AuthOptions>().BindConfiguration(AuthOptions.Section).ValidateOnStart();
        services.AddSingleton<IValidateOptions<AuthOptions>, AuthOptionsValidator>();
        services.AddSingleton<IPasswordHasher, Argon2idPasswordHasher>();
        services.AddSingleton<IPasswordPolicy, PasswordPolicy>();
        services.AddSingleton<IPasswordWork, PasswordWork>();
        return services;
    }
}
