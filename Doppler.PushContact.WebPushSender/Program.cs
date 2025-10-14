using Doppler.PushContact.QueuingService.MessageQueueBroker;
using Doppler.PushContact.Transversal;
using Doppler.PushContact.WebPushSender.Logging;
using Doppler.PushContact.WebPushSender.Repositories.Setup;
using Doppler.PushContact.WebPushSender.Senders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;
using System;

namespace Doppler.PushContact.WebPushSender
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var host = CreateHostBuilder(args).Build();
            host.Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .UseSerilog((hostContext, loggerConfiguration) =>
                {
                    loggerConfiguration.SetupSeriLog(hostContext.Configuration, hostContext.HostingEnvironment);
                })
                .ConfigureAppConfiguration((hostContext, configurationBuilder) =>
                {
                    // It is if you want to override the configuration in your
                    // local environment, `*.Secret.*` files will not be included in git.
                    configurationBuilder.AddJsonFile("appsettings.Secret.json", true);

                    // It is to override configuration using Docker's services.
                    // Probably this will be the way of overriding the configuration in our Swarm stack.
                    configurationBuilder.AddJsonFile("/run/secrets/appsettings.Secret.json", true);

                    configurationBuilder.AddEnvironmentVariables();
                })
                .ConfigureServices((hostContext, services) =>
                {
                    var configuration = hostContext.Configuration;
                    services.AddMessageQueueBroker(configuration);

                    services
                        .Configure<WebPushSenderSettings>(configuration.GetSection(nameof(WebPushSenderSettings)))
                        .Configure<IOptions<WebPushSenderSettings>>(
                            options => options.Value.Type = Enum.Parse<WebPushSenderTypes>(
                                configuration.GetSection(nameof(WebPushSenderSettings)).GetSection("Type").Value, true)
                            );

                    services.AddSingleton<IWebPushSenderFactory, WebPushSenderFactory>();

                    // Register IWebPushSender's
                    services.AddSingleton<IWebPushSender, DefaultWebPushSender>();

                    // Initialize EncryptionHelper
                    services.Configure<EncryptionSettings>(configuration.GetSection("EncryptionSettings"));
                    var encryptionSettings = configuration.GetSection("EncryptionSettings").Get<EncryptionSettings>();
                    EncryptionHelper.Initialize(encryptionSettings.Key, encryptionSettings.IV);

                    services.AddMongoDBRepositoryService(configuration);

                    services.AddHostedService<SenderExecutor>();
                });
    }
}
