using Doppler.PushContact.DopplerSecurity;
using Doppler.PushContact.QueuingService.MessageQueueBroker;
using Doppler.PushContact.Repositories;
using Doppler.PushContact.Repositories.Interfaces;
using Doppler.PushContact.Services;
using Doppler.PushContact.Services.Messages;
using Doppler.PushContact.Services.Queue;
using Doppler.PushContact.Transversal;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using System;

namespace Doppler.PushContact
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            // Initialize EncryptionHelper
            services.Configure<EncryptionSettings>(Configuration.GetSection("EncryptionSettings"));
            var encryptionSettings = Configuration.GetSection("EncryptionSettings").Get<EncryptionSettings>();
            EncryptionHelper.Initialize(encryptionSettings.Key, encryptionSettings.IV);

            services.AddDopplerSecurity();
            services.AddHttpContextAccessor();
            services.AddPushMongoContext(Configuration);
            services.AddScoped<IPushApiTokenGetter, PushApiTokenGetter>();
            services.AddPushServices(Configuration);
            services.AddMessageSender(Configuration);
            services.AddMessageQueueBroker(Configuration);
            services.AddScoped<IWebPushPublisherService, WebPushPublisherService>();
            services.AddScoped<IMessageRepository, MessageRepository>();
            services.AddScoped<IWebPushEventRepository, WebPushEventRepository>();
            services.AddScoped<IPushContactRepository, PushContactRepository>();
            services.AddScoped<IWebPushEventService, WebPushEventService>();
            services.AddSingleton<IBackgroundQueue, BackgroundQueue>();
            services.AddHostedService<QueueBackgroundService>();
            services.AddControllers();
            services.AddCors();
            services.AddSwaggerGen(c =>
            {
                c.AddSecurityDefinition("Bearer",
                    new OpenApiSecurityScheme
                    {
                        In = ParameterLocation.Header,
                        Description = "Please enter the token into field as 'Bearer {token}'",
                        Name = "Authorization",
                        Type = SecuritySchemeType.ApiKey,
                        Scheme = "Bearer"
                    });
                c.AddSecurityRequirement(new OpenApiSecurityRequirement
                    {
                        {
                            new OpenApiSecurityScheme
                            {
                                Reference = new OpenApiReference { Id = "Bearer", Type = ReferenceType.SecurityScheme },
                            },
                            Array.Empty<string>()
                        }
                    });

                c.SwaggerDoc("v1", new OpenApiInfo { Title = "Doppler.PushContact", Version = "v1" });

                var baseUrl = Configuration.GetValue<string>("BaseURL");
                if (!string.IsNullOrEmpty(baseUrl))
                {
                    c.AddServer(new OpenApiServer() { Url = baseUrl });
                };
            });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseSwagger();
            app.UseSwaggerUI(c => c.SwaggerEndpoint("v1/swagger.json", "Doppler.PushContact v1"));

            app.UseStaticFiles();

            app.UseRouting();

            app.UseCors(policy => policy
                .SetIsOriginAllowed(isOriginAllowed: _ => true)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials());

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }
}
