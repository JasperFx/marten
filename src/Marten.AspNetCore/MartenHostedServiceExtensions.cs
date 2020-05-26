using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Marten.AspNetCore
{
    public static class MartenHostedServiceExtensions
    {
        public static IServiceCollection AddMartenHostedService(this IServiceCollection services)
        {
            return services.AddSingleton<IHostedService, MartenHostedService>();
        }
    }
}
