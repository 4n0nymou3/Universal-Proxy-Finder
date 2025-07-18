namespace UniversalProxyFinder.Config;

public sealed class AppSettings
{
    public string GithubToken { get; }
    public string GithubUser { get; }
    public string GithubRepo { get; }
    public string[] ProxySources { get; }
    public string V2rayResultPath { get; }
    public string SingboxResultPath { get; }
    public int ConnectionTimeoutMs { get; }
    public int MaxThreads { get; }
    public string SingboxPath { get; }

    public AppSettings()
    {
        GithubToken = GetRequiredEnvironmentVariable("GH_TOKEN");
        GithubUser = GetRequiredEnvironmentVariable("GITHUB_USER");
        GithubRepo = GetRequiredEnvironmentVariable("GITHUB_REPO");
        ProxySources = GetRequiredEnvironmentVariable("PROXY_SOURCES_URL").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        V2rayResultPath = GetRequiredEnvironmentVariable("V2RAY_RESULT_PATH");
        SingboxResultPath = GetRequiredEnvironmentVariable("SINGBOX_RESULT_PATH");
        ConnectionTimeoutMs = int.Parse(GetRequiredEnvironmentVariable("CONNECTION_TIMEOUT_MS"));
        MaxThreads = int.Parse(GetRequiredEnvironmentVariable("MAX_THREADS"));
        SingboxPath = GetRequiredEnvironmentVariable("SINGBOX_EXECUTABLE_PATH");
    }

    private static string GetRequiredEnvironmentVariable(string variableName)
    {
        var value = Environment.GetEnvironmentVariable(variableName);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ApplicationException($"Configuration error: The required environment variable '{variableName}' is not set.");
        }
        return value;
    }
}