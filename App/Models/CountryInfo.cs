namespace UniversalProxyFinder.Models;

public class CountryInfo
{
    public string CountryCode { get; set; } = "Unknown";
    public string CountryName { get; set; } = "Unknown";
    public string CountryFlag => IsoCountryCodeToFlagEmoji(CountryCode);

    private static string IsoCountryCodeToFlagEmoji(string countryCode)
    {
        if (string.IsNullOrEmpty(countryCode) || countryCode.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            return "❓";
        }

        return string.Concat(countryCode
            .ToUpper()
            .Select(c => char.ConvertFromUtf32(c + 0x1F1A5)));
    }
}