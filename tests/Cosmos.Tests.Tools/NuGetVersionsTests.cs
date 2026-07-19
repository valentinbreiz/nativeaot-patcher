using Cosmos.Tools.Update;

namespace Cosmos.Tests.Tools;

public class NuGetVersionsTests
{
    [Fact]
    public void PickLatest_FiltersPrereleaseByDefault()
    {
        string? latest = NuGetVersions.PickLatest(["3.0.70", "3.0.71", "3.1.0-rc.1"], includePrerelease: false);
        Assert.Equal("3.0.71", latest);
    }

    [Fact]
    public void PickLatest_IncludesPrereleaseWhenAsked()
    {
        string? latest = NuGetVersions.PickLatest(["3.0.70", "3.0.71", "3.1.0-rc.1"], includePrerelease: true);
        Assert.Equal("3.1.0-rc.1", latest);
    }

    [Fact]
    public void PickLatest_StableOutranksPrereleaseWithSameNumericPart()
    {
        string? latest = NuGetVersions.PickLatest(["3.1.0-rc.1", "3.1.0"], includePrerelease: true);
        Assert.Equal("3.1.0", latest);
    }

    [Fact]
    public void PickLatest_FourPartDevStampOrdersAboveItsBaseRelease()
    {
        string? latest = NuGetVersions.PickLatest(["3.0.71", "3.0.71.20260719"], includePrerelease: false);
        Assert.Equal("3.0.71.20260719", latest);
    }

    [Fact]
    public void PickLatest_UnparsableAndEmptyInputYieldNull()
    {
        Assert.Null(NuGetVersions.PickLatest(["abc", ""], includePrerelease: true));
        Assert.Null(NuGetVersions.PickLatest([], includePrerelease: true));
    }

    [Theory]
    [InlineData("3.0.72", "3.0.71", true)]
    [InlineData("3.0.71", "3.0.72", false)]
    [InlineData("3.0.71", "3.0.71", false)]
    public void IsNewer_OrdersReleaseVersions(string candidate, string current, bool expected)
    {
        Assert.Equal(expected, NuGetVersions.IsNewer(candidate, current));
    }

    [Fact]
    public void IsNewer_DevStampIsNotOutdatedByItsBaseRelease()
    {
        // A machine running a locally built 3.0.71.20260719 must not be nagged
        // to "update" to the 3.0.71 release it was derived from.
        Assert.False(NuGetVersions.IsNewer("3.0.71", "3.0.71.20260719"));
        Assert.True(NuGetVersions.IsNewer("3.0.72", "3.0.71.20260719"));
    }

    [Fact]
    public void IsNewer_StableIsNewerThanItsOwnPrerelease()
    {
        Assert.True(NuGetVersions.IsNewer("3.1.0", "3.1.0-rc.1"));
        Assert.False(NuGetVersions.IsNewer("3.1.0-rc.1", "3.1.0"));
    }

    [Fact]
    public void IsNewer_OrdersPrereleasesOfTheSameBase()
    {
        Assert.True(NuGetVersions.IsNewer("3.1.0-rc.2", "3.1.0-rc.1"));
        Assert.False(NuGetVersions.IsNewer("3.1.0-rc.1", "3.1.0-rc.2"));
        // Numeric prerelease identifiers compare numerically, not ordinally.
        Assert.True(NuGetVersions.IsNewer("3.1.0-beta.10", "3.1.0-beta.9"));
        // SemVer: alpha < alpha.1 < beta (numeric identifiers below alphanumeric).
        Assert.True(NuGetVersions.IsNewer("3.1.0-alpha.1", "3.1.0-alpha"));
        Assert.True(NuGetVersions.IsNewer("3.1.0-beta", "3.1.0-alpha.1"));
    }

    [Fact]
    public void PickLatest_OrdersDottedNumericPrereleaseTags()
    {
        string? latest = NuGetVersions.PickLatest(["3.1.0-beta.9", "3.1.0-beta.10"], includePrerelease: true);
        Assert.Equal("3.1.0-beta.10", latest);

        latest = NuGetVersions.PickLatest(["3.1.0-beta.10", "3.1.0-beta.9"], includePrerelease: true);
        Assert.Equal("3.1.0-beta.10", latest);
    }

    [Theory]
    [InlineData("3.0.72", true)]
    [InlineData("3.0.71.20260719", true)]
    [InlineData("3.1.0-rc.1", true)]
    [InlineData("3.0.7l", false)]
    [InlineData("not-a-version", false)]
    [InlineData("3.0.72 --something", false)]
    [InlineData("3.0.72&whoami", false)]
    public void IsValidVersionRequest_GatesUserInput(string version, bool expected)
    {
        Assert.Equal(expected, NuGetVersions.IsValidVersionRequest(version));
    }

    [Fact]
    public void IsNewer_TrailingZeroRevisionIsNotNewer()
    {
        Assert.False(NuGetVersions.IsNewer("3.0.71.0", "3.0.71"));
        Assert.False(NuGetVersions.IsNewer("3.0.71", "3.0.71.0"));
    }

    [Fact]
    public void TryParseNumeric_StripsPrereleaseAndMetadataSuffixes()
    {
        Assert.True(NuGetVersions.TryParseNumeric("3.0.71+abc123", out Version withMetadata));
        Assert.Equal(new Version(3, 0, 71, 0), withMetadata);

        Assert.True(NuGetVersions.TryParseNumeric("3.1.0-rc.1", out Version withPrerelease));
        Assert.Equal(new Version(3, 1, 0, 0), withPrerelease);

        Assert.False(NuGetVersions.TryParseNumeric(null, out _));
        Assert.False(NuGetVersions.TryParseNumeric("not-a-version", out _));
    }
}
