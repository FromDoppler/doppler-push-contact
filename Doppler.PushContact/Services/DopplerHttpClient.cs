using Flurl;
using Flurl.Http;
using Microsoft.Extensions.Options;
using System;
using System.Threading.Tasks;

namespace Doppler.PushContact.Services
{
    public class DopplerHttpClient : IDopplerHttpClient
    {
        private readonly DopplerHttpClientSettings _dopplerHttpClientSettings;

        public DopplerHttpClient(IOptions<DopplerHttpClientSettings> dopplerHttpClientSettings)
        {
            _dopplerHttpClientSettings = dopplerHttpClientSettings.Value;
        }

        public async Task<bool> RegisterVisitorSafeAsync(string domain, string visitorGuid, string email)
        {
            if (string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(visitorGuid) || string.IsNullOrWhiteSpace(email))
            {
                return false;
            }

            if (_dopplerHttpClientSettings == null || string.IsNullOrWhiteSpace(_dopplerHttpClientSettings.DopplerAppServer))
            {
                return false;
            }

            try
            {
                var response = await _dopplerHttpClientSettings.DopplerAppServer
                    .AppendPathSegments("Lists", "InternalSubscriber", "SaveVisitorWithEmail")
                    .WithHeader("Origin", _dopplerHttpClientSettings.PushContactApiOrigin) // TODO: review origin setting
                    .PostJsonAsync(new
                    {
                        Domain = domain,
                        VisitorGuid = visitorGuid,
                        Email = email,
                    });

                if (response.StatusCode >= 200 && response.StatusCode < 300)
                {
                    return true;
                }

                var errorText = await response.ResponseMessage.Content.ReadAsStringAsync();
                //_logger.LogError("Fallo el registro del contacto en Doppler. Status: {StatusCode}, Respuesta: {Body}", response.StatusCode, errorText);
                return false;
            }
            catch (FlurlHttpException ex)
            {
                var errorText = await ex.GetResponseStringAsync();
                //_logger.LogError(ex, "Excepción al llamar al endpoint de Doppler. Detalles: {Body}", errorText);
                return false;
            }
            catch (Exception ex)
            {
                //_logger.LogError(ex, "Excepción inesperada al registrar el contacto en Doppler.");
                return false;
            }
        }
    }
}
