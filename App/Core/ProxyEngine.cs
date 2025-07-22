using Octokit;
using SingBoxLib.Configuration;
using SingBoxLib.Configuration.Inbound;
using SingBoxLib.Configuration.Outbound;
using SingBoxLib.Configuration.Outbound.Abstract;
using SingBoxLib.Parsing;
using SingBoxLib.Runtime;
using SingBoxLib.Runtime.Testing;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using UniversalProxyFinder.Config;
using UniversalProxyFinder.Models;

namespace UniversalProxyFinder.Core;

public sealed class ProxyEngine
{
    private readonly AppSettings _settings;
    private readonly GeoLocator _geoLocator;
    
    public ProxyEngine()
    {
        _settings = new AppSettings();
        _geoLocator = new GeoLocator();
    }

    public async Task StartAsync()
    {
        var startTime = DateTime.Now;
        Log("Engine started.");

        var collectedProfiles = (await CollectProfilesAsync()).DistinctBy(p => p.ToProfileUrl()).ToList();
        Log($"Collected {collectedProfiles.Count} unique profiles in total.");

        var workingResults = await TestProfilesAsync(collectedProfiles);
        Log($"Testing finished, found {workingResults.Count} working profiles.");

        if (workingResults.Any())
        {
            Log("Formatting and sorting results...");
            var finalResults = await FormatAndSortResultsAsync(workingResults);
            
            Log($"Uploading {finalResults.Count} profiles to GitHub...");
            await CommitResultsToGithubAsync(finalResults);
        }

        var timeSpent = DateTime.Now - startTime;
        Log($"Job finished in {timeSpent.Minutes:00}m {timeSpent.Seconds:00}s.");
    }
    
    private async Task<IReadOnlyCollection<ProfileItem>> CollectProfilesAsync()
    {
        var profiles = new ConcurrentBag<ProfileItem>();
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        await Parallel.ForEachAsync(_settings.ProxySources, new ParallelOptions { MaxDegreeOfParallelism = _settings.MaxThreads }, async (source, ct) =>
        {
            try
            {
                var content = await client.GetStringAsync(source, ct);
                
                string decodedContent;
                try
                {
                    var bytes = Convert.FromBase64String(content);
                    decodedContent = Encoding.UTF8.GetString(bytes);
                }
                catch (FormatException)
                {
                    decodedContent = content;
                }

                using var reader = new StringReader(decodedContent);
                string? line;
                var count = 0;
                while ((line = await reader.ReadLineAsync(ct)) is not null)
                {
                    var trimmedLine = line.Trim();
                    if (string.IsNullOrEmpty(trimmedLine)) continue;

                    try
                    {
                        var profile = ProfileParser.ParseProfileUrl(trimmedLine);
                        if (profile != null)
                        {
                            profiles.Add(profile);
                            count++;
                        }
                    }
                    catch
                    {
                    }
                }
                if (count > 0)
                {
                    Log($"Collected {count} profiles from {source}");
                }
            }
            catch (Exception ex)
            {
                Log($"Failed to fetch from {source}. Error: {ex.Message}");
            }
        });
        return profiles;
    }

    private async Task<IReadOnlyCollection<UrlTestResult>> TestProfilesAsync(IReadOnlyCollection<ProfileItem> profiles)
    {
        var tester = new ParallelUrlTester(
            new SingBoxWrapper(_settings.SingboxPath),
            20000,
            _settings.MaxThreads,
            _settings.ConnectionTimeoutMs,
            1024,
            "https://www.youtube.com/generate_204");

        var workingResults = new ConcurrentBag<UrlTestResult>();
        await tester.ParallelTestAsync(profiles, new Progress<UrlTestResult>(result =>
        {
            if (result.Success)
            {
                workingResults.Add(result);
            }
        }), CancellationToken.None);
        
        return workingResults;
    }

    private async Task<List<ProfileItem>> FormatAndSortResultsAsync(IReadOnlyCollection<UrlTestResult> workingResults)
    {
        var geoLocatedResults = new List<(UrlTestResult TestResult, CountryInfo CountryInfo)>();
        foreach (var result in workingResults)
        {
            var countryInfo = await _geoLocator.GetCountryAsync(result.Profile.Address!);
            if (countryInfo is not null && !string.IsNullOrWhiteSpace(countryInfo.CountryCode) && countryInfo.CountryCode != "Unknown")
            {
                geoLocatedResults.Add((result, countryInfo));
            }
        }
    
        return geoLocatedResults
            .GroupBy(p => p.CountryInfo.CountryCode)
            .Select(countryGroup => countryGroup
                .OrderBy(p => p.TestResult.Delay)
                .WithIndex()
                .Select(indexedProfile =>
                {
                    var profile = indexedProfile.Item.TestResult.Profile;
                    var countryInfo = indexedProfile.Item.CountryInfo;
                    profile.Name = $"{countryInfo.CountryFlag} {countryInfo.CountryCode} {indexedProfile.Index + 1}";
                    return profile;
                })
            )
            .SelectMany(group => group)
            .Take(200)
            .ToList();
    }

    private async Task CommitResultsToGithubAsync(List<ProfileItem> profiles)
    {
        var v2raySubscription = new StringBuilder();
        foreach (var profile in profiles)
        {
            v2raySubscription.AppendLine(profile.ToProfileUrl());
        }

        await UploadFileAsync(_settings.V2rayResultPath, v2raySubscription.ToString());

        var singboxConfig = CreateSingboxConfig(profiles);
        await UploadFileAsync(_settings.SingboxResultPath, singboxConfig.ToJson());
    }

    private SingBoxConfig CreateSingboxConfig(List<ProfileItem> profiles)
    {
        var outbounds = new List<OutboundConfig>();
        foreach (var profile in profiles)
        {
            var outbound = profile.ToOutboundConfig();
            outbound.Tag = profile.Name;
            outbounds.Add(outbound);
        }

        var allTags = outbounds.Select(p => p.Tag).Where(t => !string.IsNullOrEmpty(t)).ToList();

        var autoGroup = new UrlTestOutbound
        {
            Tag = "auto",
            Outbounds = allTags!,
            Interval = "10m",
            Tolerance = 200,
            Url = "https://www.youtube.com/generate_204"
        };

        var selectorGroup = new SelectorOutbound
        {
            Tag = "select",
            Outbounds = ["auto", ..allTags!],
            Default = "auto"
        };
        
        outbounds.Add(autoGroup);
        outbounds.Add(selectorGroup);

        return new SingBoxConfig
        {
            Outbounds = outbounds,
            Inbounds =
            [
                new TunInbound
                {
                    InterfaceName = "tun0",
                    Address = ["172.19.0.1/30"],
                    Mtu = 1500,
                    AutoRoute = true,
                    Stack = TunStacks.System,
                    EndpointIndependentNat = true,
                    StrictRoute = true,
                },
                new MixedInbound
                {
                    Listen = "127.0.0.1",
                    ListenPort = 2080,
                }
            ],
            Route = new()
            {
                Final = "select",
                AutoDetectInterface = true,
            }
        };
    }

    private async Task UploadFileAsync(string path, string content)
    {
        var client = new GitHubClient(new ProductHeaderValue(_settings.GithubRepo))
        {
            Credentials = new Credentials(_settings.GithubToken)
        };
        
        string? sha = null;
        try
        {
            var existingFile = await client.Repository.Content.GetAllContents(_settings.GithubUser, _settings.GithubRepo, path);
            sha = existingFile.FirstOrDefault()?.Sha;
        }
        catch (Octokit.NotFoundException)
        {
        }
        catch (Exception ex)
        {
            Log($"An error occurred while checking for existing file on GitHub: {ex.Message}");
            return;
        }

        try
        {
            if (sha is null)
            {
                await client.Repository.Content.CreateFile(_settings.GithubUser, _settings.GithubRepo, path, new CreateFileRequest($"Create subscriptions: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC", content));
                Log($"Successfully created file: {path}");
            }
            else
            {
                await client.Repository.Content.UpdateFile(_settings.GithubUser, _settings.GithubRepo, path, new UpdateFileRequest($"Update subscriptions: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC", content, sha));
                Log($"Successfully updated file: {path}");
            }
        }
        catch (Exception ex)
        {
            Log($"An error occurred while uploading to GitHub: {ex.Message}");
        }
    }

    private void Log(string message)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] - {message}");
    }
}

public static class HelperExtensions
{
    public static IEnumerable<(int Index, T Item)> WithIndex<T>(this IEnumerable<T> items)
    {
        int index = 0;
        foreach (var item in items)
        {
            yield return (index++, item);
        }
    }
}