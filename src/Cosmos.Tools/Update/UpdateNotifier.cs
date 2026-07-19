using System.Text.Json;
using Cosmos.Tools.Platform;
using Spectre.Console;

namespace Cosmos.Tools.Update;

/// <summary>
/// Once-a-day "new version available" notice. The check result is cached in
/// the Cosmos state directory so at most one NuGet query happens per day, and
/// every failure path (offline, rate-limited, corrupt state) is silent — the
/// notice must never break, slow down, or pollute the actual command.
/// </summary>
public static class UpdateNotifier
{
    private const string DisableEnvVar = "COSMOS_NO_UPDATE_NOTIFIER";
    private static readonly TimeSpan s_checkInterval = TimeSpan.FromHours(24);
    private static readonly TimeSpan s_checkTimeout = TimeSpan.FromMilliseconds(2500);

    private sealed class State
    {
        public DateTimeOffset LastCheckedUtc { get; set; }
        public string? LatestVersion { get; set; }
    }

    public static string StateFilePath => Path.Combine(ToolChecker.GetCosmosRootPath(), "update-check.json");

    /// <summary>
    /// Prints a one-line update notice when a newer Cosmos.Tools is known to exist.
    /// Suppressed in CI, when output is redirected (piped/parsed output must stay
    /// clean), and via COSMOS_NO_UPDATE_NOTIFIER. Callers must additionally skip
    /// this in their own machine-readable modes (--json).
    /// </summary>
    public static async Task MaybeNotifyAsync()
    {
        try
        {
            if (Environment.GetEnvironmentVariable(DisableEnvVar) != null
                || Environment.GetEnvironmentVariable("CI") != null
                || Console.IsOutputRedirected)
            {
                return;
            }

            State state = LoadState() ?? new State();
            string current = NuGetVersions.CurrentCliVersion();

            if (DateTimeOffset.UtcNow - state.LastCheckedUtc >= s_checkInterval)
            {
                // Stamp before querying so an offline machine retries once a day,
                // not on every command.
                state.LastCheckedUtc = DateTimeOffset.UtcNow;

                using CancellationTokenSource timeout = new CancellationTokenSource(s_checkTimeout);
                using HttpClient http = new HttpClient();
                http.DefaultRequestHeaders.Add("User-Agent", "Cosmos-Tools");

                string? latest = await NuGetVersions.GetLatestVersionAsync(
                    http, "Cosmos.Tools", includePrerelease: current.Contains('-'), timeout.Token);
                if (latest != null)
                {
                    state.LatestVersion = latest;
                }

                SaveState(state);
            }

            if (state.LatestVersion is string known && NuGetVersions.IsNewer(known, current))
            {
                AnsiConsole.MarkupLine(
                    $"  [yellow]Cosmos {Markup.Escape(known)} is available[/] [dim](installed {Markup.Escape(current)})[/] — run [blue]cosmos update[/].");
                AnsiConsole.WriteLine();
            }
        }
        catch
        {
            // Never let the notifier interfere with the command that ran.
        }
    }

    /// <summary>
    /// Records that an update just ran so the notice disappears immediately
    /// instead of lingering until the next 24h check.
    /// </summary>
    public static void RecordUpdated(string? latestVersion)
    {
        SaveState(new State
        {
            LastCheckedUtc = DateTimeOffset.UtcNow,
            LatestVersion = latestVersion ?? NuGetVersions.CurrentCliVersion()
        });
    }

    private static State? LoadState()
    {
        try
        {
            string path = StateFilePath;
            return File.Exists(path) ? JsonSerializer.Deserialize<State>(File.ReadAllText(path)) : null;
        }
        catch
        {
            return null;
        }
    }

    private static void SaveState(State state)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StateFilePath)!);
            File.WriteAllText(StateFilePath, JsonSerializer.Serialize(state));
        }
        catch
        {
            // A read-only home directory just means the check re-runs next time.
        }
    }
}
