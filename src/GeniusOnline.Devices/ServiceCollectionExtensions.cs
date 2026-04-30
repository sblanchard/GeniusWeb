using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GeniusOnline.Devices;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGeniusDevices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<DeviceRegistryOptions>(configuration.GetSection("Devices"));
        services.AddSingleton<DeviceRegistry>();
        services.AddHostedService<DeviceRegistryHost>();
        return services;
    }
}
