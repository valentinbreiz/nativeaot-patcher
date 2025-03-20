using Liquip.Asm.Build.Tasks;
using Microsoft.Build.Framework;
using Microsoft.VisualStudio.TestPlatform.CoreUtilities.Helpers;
using Moq;
using Xunit.Sdk;

namespace Liquip.Asm.Build.Test;

public class UnitTest1
{
    private Mock<IBuildEngine> buildEngine;
    private List<BuildErrorEventArgs> errors;

    public UnitTest1()
    {
        buildEngine = new Mock<IBuildEngine>();
        errors = [];
        buildEngine.Setup(x => x.LogErrorEvent(It.IsAny<BuildErrorEventArgs>()))
            .Callback<BuildErrorEventArgs>(e => errors.Add(e));
    }

    [Theory]
    [InlineData("/usr/bin/yasm", PlatformID.Unix)]
    public void Test1(string path, PlatformID platform)
    {
        if (Environment.OSVersion.Platform != platform)
            throw SkipException.ForSkip("skiping this test");

        YasmBuildTask yasm = new()
        {
            YasmPath = path, SearchPath = ["./asm/"], OutputPath = "./output", BuildEngine = buildEngine.Object
        };


        bool success = yasm.Execute();

        Assert.True(success);
    }
}
