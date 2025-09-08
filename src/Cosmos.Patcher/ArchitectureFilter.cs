using System;
using System.Linq;
using Cosmos.Patcher.Logging;
using Mono.Cecil;

namespace Cosmos.Patcher;

public sealed class ArchitectureFilter
{
    private readonly IBuildLogger _log;
    private const string PlatformSpecificAttributeFullName = "Cosmos.Build.API.Attributes.PlatformSpecificAttribute";
    private readonly string _targetArchitecture;

    public ArchitectureFilter(string targetArchitecture, IBuildLogger? logger = null)
    {
        _log = logger ?? new ConsoleBuildLogger();
        _targetArchitecture = targetArchitecture?.ToLowerInvariant() ?? "x64";
        _log.Debug($"[ArchitectureFilter] Initialized for architecture: {_targetArchitecture}");
    }

    public bool ShouldIncludeType(TypeDefinition type)
    {
        var platformAttr = type.CustomAttributes
            .FirstOrDefault(a => a.AttributeType.FullName == PlatformSpecificAttributeFullName);

        if (platformAttr == null)
        {
            // No platform attribute means it's platform-agnostic
            return true;
        }

        // Check if the architecture matches
        var architectureValue = GetArchitectureValue(platformAttr);
        return IsArchitectureMatch(architectureValue);
    }

    public bool ShouldIncludeMethod(MethodDefinition method)
    {
        var platformAttr = method.CustomAttributes
            .FirstOrDefault(a => a.AttributeType.FullName == PlatformSpecificAttributeFullName);

        if (platformAttr == null)
        {
            // Check if the declaring type has platform restrictions
            return ShouldIncludeType(method.DeclaringType);
        }

        var architectureValue = GetArchitectureValue(platformAttr);
        return IsArchitectureMatch(architectureValue);
    }

    private int GetArchitectureValue(CustomAttribute attribute)
    {
        if (attribute.ConstructorArguments.Count > 0)
        {
            var arg = attribute.ConstructorArguments[0];
            if (arg.Value is int intValue)
            {
                return intValue;
            }
        }

        return 0; // None
    }

    private bool IsArchitectureMatch(int architectureFlags)
    {
        // PlatformArchitecture enum values:
        // None = 0, X64 = 1, ARM64 = 2, RISCV64 = 4, All = 7

        return _targetArchitecture switch
        {
            "x64" or "x86_64" => (architectureFlags & 1) != 0,
            "arm64" or "aarch64" => (architectureFlags & 2) != 0,
            "riscv64" => (architectureFlags & 4) != 0,
            _ => true // Unknown architecture, include everything
        };
    }

    public void FilterPlugs(ref List<TypeDefinition> plugs)
    {
        var originalCount = plugs.Count;
        plugs = plugs.Where(ShouldIncludeType).ToList();

        if (originalCount != plugs.Count)
        {
            _log.Info($"[ArchitectureFilter] Filtered {originalCount - plugs.Count} plugs for architecture {_targetArchitecture}");
        }
    }
}
