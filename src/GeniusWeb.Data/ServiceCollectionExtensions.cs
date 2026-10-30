using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GeniusWeb.Data;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGeniusData(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var conn = configuration.GetConnectionString("Default")
                   ?? "Data Source=geniusweb.db";

        services.AddDbContextFactory<GeniusDbContext>(opts => opts.UseSqlite(conn));
        return services;
    }
}
