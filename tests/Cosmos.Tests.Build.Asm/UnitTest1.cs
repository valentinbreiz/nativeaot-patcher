using Cosmos.Build.Asm.Tasks;
using Microsoft.Build.Framework;
using Moq;
using Xunit.Sdk;

namespace Cosmos.Build.Asm.Tests;

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

    private static string GetClangPath()
    {
        var paths = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator);
        if (paths != null)
        {
            string exeName = Environment.OSVersion.Platform == PlatformID.Win32NT ? "clang.exe" : "clang";
            foreach (var p in paths)
            {
                var fullPath = Path.Combine(p, exeName);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
        }
        return "clang";
    }

    [Fact]
    public void Test1()
    {
        AsmBuildTask asm = new()
        {
            ClangPath = GetClangPath(),
            SourceFiles = ["./asm/test.s"],
            OutputPath = "./output",
            TargetArchitecture = "x64",
            BuildEngine = buildEngine.Object
        };


        bool success = asm.Execute();

        Assert.True(success);
    }
}
