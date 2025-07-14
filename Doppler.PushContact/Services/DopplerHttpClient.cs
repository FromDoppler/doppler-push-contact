using Flurl;
using Flurl.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Threading.Tasks;

namespace Doppler.PushContact.Services
{
    public class DopplerHttpClient : IDopplerHttpClient
    {
        private readonly DopplerHttpClientSettings _dopplerHttpClientSettings;
        private readonly ILogger<DopplerHttpClient> _logger;

        public DopplerHttpClient(
            IOptions<DopplerHttpClientSettings> dopplerHttpClientSettings,
            ILogger<DopplerHttpClient> logger
        )
        {
            _dopplerHttpClientSettings = dopplerHttpClientSettings.Value;
            _logger = logger;
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
                    .WithHeader("X-Internal-Token", _dopplerHttpClientSettings.InternalToken)
                    .AllowAnyHttpStatus()
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

                var errorText = await GetErrorMessageFromResponseAsync(response);

                _logger.LogError(
                    "Doppler contact registration failed. Status: {StatusCode}, Response: {Body}, Domain: {Domain}, VisitorGuid: {VisitorGuid}, Email: {ContactEmail}",
                    response.StatusCode,
                    errorText,
                    domain,
                    visitorGuid,
                    email
                );
                return false;
            }
            catch (FlurlHttpException ex)
            {
                var errorText = await GetFlurlErrorMessageAsync(ex);
                _logger.LogError(
                    ex,
                    "Unexpected error calling the Doppler endpoint. Response: {Response}, Domain: {Domain}, VisitorGuid: {VisitorGuid}, Email: {ContactEmail}",
                    errorText,
                    domain,
                    visitorGuid,
                    email
                );
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Unexpected error registering a Doppler contact. Domain: {Domain}, VisitorGuid: {VisitorGuid}, Email: {ContactEmail}",
                    domain,
                    visitorGuid,
                    email
                );
                return false;
            }
        }

        private static async Task<string> GetFlurlErrorMessageAsync(FlurlHttpException ex)
        {
            if (ex.Call.Response != null)
            {
                var responseText = await ex.GetResponseStringAsync();
                if (!string.IsNullOrWhiteSpace(responseText))
                {
                    return responseText;
                }
            }

            return ex.Message;
        }

        private static async Task<string> GetErrorMessageFromResponseAsync(IFlurlResponse response)
        {
            if (response?.ResponseMessage?.Content != null)
            {
                var body = await response.ResponseMessage.Content.ReadAsStringAsync();
                if (!string.IsNullOrWhiteSpace(body))
                {
                    return body;
                }
            }

            return response?.ResponseMessage?.ReasonPhrase ?? "No response body";
        }
    }
}
