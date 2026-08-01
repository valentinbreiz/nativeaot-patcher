namespace Cosmos.Tests.BuildCache;

/// <summary>
/// Build hygiene tests — the kernel build output must stay free of MSBuild
/// authoring warnings so real warnings and errors remain visible.
/// Same collection as the cache tests so DevKernel builds never run in parallel.
/// </summary>
[Collection("BuildCache")]
public class BuildWarningTests : IClassFixture<BuildFixture>
{
    private readonly BuildFixture _fixture;

    public BuildWarningTests(BuildFixture fixture)
    {
        _fixture = fixture;
    }

    // ==================================================================
    // Issue #391: Cosmos.Build.Common.props and Cosmos.Architecture.props
    // both imported Cosmos.ArchitecturePicker.props, so every kernel build
    // logged MSB4011 "cannot be imported again" for the second import.
    // ==================================================================
    [Fact]
    public void Build_EmitsNoDuplicateImportWarning()
    {
        BuildResult result = _fixture.Build();

        Assert.True(result.Success, $"Build failed:\n{result.Output}");
        Assert.DoesNotContain("MSB4011", result.Output);
        Assert.DoesNotContain("cannot be imported again", result.Output);
    }
}
