using System.Reflection;
using System.Text.Json;

namespace Cosmos.Tools.Update;

/// <summary>
/// Latest-version lookup against nuget.org plus the version-ordering rules Cosmos
/// needs: release versions are 3-part (3.0.71) while dev builds are 4-part
/// date-stamped (3.0.71.20260719), and a dev stamp must never be reported as
/// outdated by the release it was derived from.
/// </summary>
public static class NuGetVersions
{
    private const string ServiceIndexUrl = "https://api.nuget.org/v3/index.json";

    /// <summary>
    /// Resolves the highest version of a package on nuget.org. Prerelease versions
    /// are skipped unless <paramref name="includePrerelease"/> is set. Returns null
    /// when the package or the network is unavailable — callers treat that as
    /// "no update", never as an error.
    /// </summary>
    public static async Task<string?> GetLatestVersionAsync(HttpClient http, string packageId, bool includePrerelease, CancellationToken cancellationToken = default)
    {
        try
        {
            // The flatcontainer base URL must come from the service index — the
            // NuGet docs reserve the right to move the PackageBaseAddress resource.
            string indexJson = await http.GetStringAsync(ServiceIndexUrl, cancellationToken);
            string? baseUrl = null;
            using (JsonDocument index = JsonDocument.Parse(indexJson))
            {
                foreach (JsonElement resource in index.RootElement.GetProperty("resources").EnumerateArray())
                {
                    if (resource.TryGetProperty("@type", out JsonElement type)
                        && type.GetString() == "PackageBaseAddress/3.0.0")
                    {
                        baseUrl = resource.GetProperty("@id").GetString();
                        break;
                    }
                }
            }

            if (baseUrl == null)
            {
                return null;
            }

            string url = $"{baseUrl.TrimEnd('/')}/{packageId.ToLowerInvariant()}/index.json";
            string versionsJson = await http.GetStringAsync(url, cancellationToken);
            using JsonDocument doc = JsonDocument.Parse(versionsJson);

            List<string> versions = new List<string>();
            foreach (JsonElement element in doc.RootElement.GetProperty("versions").EnumerateArray())
            {
                if (element.GetString() is string version)
                {
                    versions.Add(version);
                }
            }

            return PickLatest(versions, includePrerelease);
        }
        catch
        {
            // Offline, rate-limited, 404 on a just-published package still
            // propagating through the CDN — all mean "no update available".
            return null;
        }
    }

    /// <summary>
    /// Picks the highest version from a flatcontainer version list. The list
    /// contains prerelease (and unlisted) versions, so stable-only filtering
    /// happens here.
    /// </summary>
    public static string? PickLatest(IEnumerable<string> versions, bool includePrerelease)
    {
        string? best = null;
        foreach (string candidate in versions)
        {
            if (!includePrerelease && candidate.Contains('-'))
            {
                continue;
            }

            if (!TryParseNumeric(candidate, out _))
            {
                continue;
            }

            if (best == null || Compare(candidate, best) > 0)
            {
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>
    /// Orders two versions: numeric parts first, then a stable version above any
    /// prerelease with the same numeric part, then prerelease identifiers by the
    /// SemVer rules (dot-separated; numeric identifiers compare numerically and
    /// order below alphanumeric ones — so rc.10 &gt; rc.9).
    /// </summary>
    public static int Compare(string a, string b)
    {
        bool aParsed = TryParseNumeric(a, out Version aNumeric);
        bool bParsed = TryParseNumeric(b, out Version bNumeric);
        if (!aParsed || !bParsed)
        {
            return aParsed == bParsed ? 0 : aParsed ? 1 : -1;
        }

        int numeric = aNumeric.CompareTo(bNumeric);
        if (numeric != 0)
        {
            return numeric;
        }

        string aPre = PrereleasePart(a);
        string bPre = PrereleasePart(b);
        if (aPre.Length == 0 || bPre.Length == 0)
        {
            return bPre.Length.CompareTo(aPre.Length);
        }

        return ComparePrerelease(aPre, bPre);
    }

    private static string PrereleasePart(string version)
    {
        int metadata = version.IndexOf('+');
        string bare = metadata >= 0 ? version[..metadata] : version;
        int dash = bare.IndexOf('-');
        return dash >= 0 ? bare[(dash + 1)..] : "";
    }

    private static int ComparePrerelease(string a, string b)
    {
        string[] aIds = a.Split('.');
        string[] bIds = b.Split('.');
        int shared = Math.Min(aIds.Length, bIds.Length);
        for (int i = 0; i < shared; i++)
        {
            bool aIsNumber = long.TryParse(aIds[i], out long aValue);
            bool bIsNumber = long.TryParse(bIds[i], out long bValue);
            int comparison = (aIsNumber, bIsNumber) switch
            {
                (true, true) => aValue.CompareTo(bValue),
                (true, false) => -1,
                (false, true) => 1,
                (false, false) => string.CompareOrdinal(aIds[i], bIds[i])
            };
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return aIds.Length.CompareTo(bIds.Length);
    }

    /// <summary>
    /// Sanity gate for a user-supplied --version: must have a parsable numeric
    /// part and stay within the NuGet version character set. This also keeps the
    /// value safe to interpolate into child-process command lines.
    /// </summary>
    public static bool IsValidVersionRequest(string version)
    {
        return TryParseNumeric(version, out _)
            && version.All(static c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '+');
    }

    /// <summary>
    /// Parses the numeric part of a version, dropping any -prerelease or +metadata
    /// suffix and padding to four components so "3.0.71" and "3.0.71.0" compare equal.
    /// </summary>
    public static bool TryParseNumeric(string? version, out Version parsed)
    {
        parsed = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(version))
        {
            return false;
        }

        string numeric = version;
        int suffix = numeric.IndexOfAny(['-', '+']);
        if (suffix >= 0)
        {
            numeric = numeric[..suffix];
        }

        if (!numeric.Contains('.'))
        {
            numeric += ".0";
        }

        if (!Version.TryParse(numeric, out Version? result))
        {
            return false;
        }

        parsed = new Version(
            result.Major,
            result.Minor,
            result.Build < 0 ? 0 : result.Build,
            result.Revision < 0 ? 0 : result.Revision);
        return true;
    }

    /// <summary>
    /// True when <paramref name="candidate"/> is strictly newer than
    /// <paramref name="current"/> under <see cref="Compare"/>.
    /// </summary>
    public static bool IsNewer(string candidate, string current)
    {
        if (!TryParseNumeric(candidate, out _) || !TryParseNumeric(current, out _))
        {
            return false;
        }

        return Compare(candidate, current) > 0;
    }

    /// <summary>
    /// The version of the running CLI as it appears on NuGet. Read from
    /// AssemblyInformationalVersion — AssemblyVersion is truncated to three parts
    /// repo-wide (see Directory.Build.props) and cannot represent the 4-part
    /// date-stamped dev versions; the "+&lt;commit&gt;" SourceLink suffix is stripped.
    /// </summary>
    public static string CurrentCliVersion()
    {
        string? informational = typeof(NuGetVersions).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(informational))
        {
            return typeof(NuGetVersions).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        }

        int metadata = informational.IndexOf('+');
        return metadata >= 0 ? informational[..metadata] : informational;
    }
}
