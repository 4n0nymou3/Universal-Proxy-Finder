using Newtonsoft.Json;
using Polly;
using Polly.Retry;
using System.Net;
using UniversalProxyFinder.Models;

namespace UniversalProxyFinder.Core;

public sealed class GeoLocator
{
    private readonly HttpClient _httpClient;
    private readonly AsyncRetryPolicy<string> _retryPolicy;

    public GeoLocator()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _retryPolicy = Policy<string>
            .Handle<HttpRequestException>()
            .Or<TaskCanceledException>()
            .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                (outcome, timespan, retryAttempt, context) =>
                {
                    Console.WriteLine($"Geo-location request failed. Waiting {timespan.TotalSeconds}s before next retry ({retryAttempt}/3).");
                });
    }

    public async Task<CountryInfo> GetCountryAsync(string address, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return new CountryInfo();
        }

        if (!IPAddress.TryParse(address, out var ipAddress))
        {
            try
            {
                var addresses = await Dns.GetHostAddressesAsync(address, cancellationToken);
                ipAddress = addresses.FirstOrDefault();
            }
            catch (Exception)
            {
                ipAddress = null;
            }
        }

        if (ipAddress is null)
        {
            return new CountryInfo();
        }

        try
        {
            string response = await _retryPolicy.ExecuteAsync(() => _httpClient.GetStringAsync($"https://api.iplocation.net/?ip={ipAddress}", cancellationToken));
            dynamic? ipInfo = JsonConvert.DeserializeObject(response);

            return new CountryInfo
            {
                CountryName = ipInfo?.country_name ?? "Unknown",
                CountryCode = ipInfo?.country_code2 ?? "Unknown"
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to get country for IP {ipAddress} after all retries. Error: {ex.Message}");
            return new CountryInfo();
        }
    }
}
