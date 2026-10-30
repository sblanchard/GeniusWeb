using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GeniusWeb.Devices;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGeniusDevices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<DeviceRegistryOptions>(configuration.GetSection("Devices"));
        services.AddSingleton<DeviceRegistry>();
        services.AddSingleton<DeviceRegistryHost>();
        services.AddHostedService(sp => sp.GetRequiredService<DeviceRegistryHost>());
        return services;
    }
}
