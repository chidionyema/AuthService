using auth.Services;
using Microsoft.Extensions.DependencyInjection;

public static class ApplicationServicesExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services
            .AddMemoryCache()
            
            .AddScoped<AuthService>()
            .AddScoped<ICurrentUserService, CurrentUserService>();
        return services;
    }
}
