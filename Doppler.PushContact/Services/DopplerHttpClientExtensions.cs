using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Doppler.PushContact.Services
{
    public static class DopplerHttpClientExtensions
    {
        public static IServiceCollection AddDopplerHttpClient(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<DopplerHttpClientSettings>(configuration.GetSection(nameof(DopplerHttpClientSettings)));

            services.AddSingleton<IDopplerHttpClient, DopplerHttpClient>();

            return services;
        }
    }
}
