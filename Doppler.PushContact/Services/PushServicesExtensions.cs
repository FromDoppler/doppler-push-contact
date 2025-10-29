using Doppler.PushContact.QueuingService.MessageQueueBroker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Doppler.PushContact.Services
{
    public static class PushServicesExtensions
    {
        public static IServiceCollection AddPushServices(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<DeviceTokenValidatorSettings>(configuration.GetSection(nameof(DeviceTokenValidatorSettings)));

            services.AddScoped<IDeviceTokenValidator, DeviceTokenValidator>();

            services.AddScoped<IPushContactService, PushContactService>();

            services.AddScoped<IDomainService, DomainService>();

            services.Configure<WebPushPublisherSettings>(configuration.GetSection(nameof(WebPushPublisherSettings)));

            services.Configure<BusinessLogicSettings>(configuration.GetSection(nameof(BusinessLogicSettings)));

            return services;
        }
    }
}
