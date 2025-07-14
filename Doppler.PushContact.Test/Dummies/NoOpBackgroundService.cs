using Microsoft.Extensions.Hosting;
using System.Threading.Tasks;
using System.Threading;

namespace Doppler.PushContact.Test.Dummies
{
    public class NoOpBackgroundService : BackgroundService
    {
        protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
    }
}
