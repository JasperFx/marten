using Microsoft.Extensions.Configuration;

namespace FreightShipping;

public static class Utils
{
    public static string? GetConnectionString()
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json")
            .Build();

        return config.GetConnectionString("Postgres");
    }
    
}