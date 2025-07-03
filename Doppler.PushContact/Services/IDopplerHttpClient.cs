using System.Threading.Tasks;

namespace Doppler.PushContact.Services
{
    public interface IDopplerHttpClient
    {
        Task<bool> RegisterVisitorSafeAsync(string domain, string visitorGuid, string email);
    }
}
