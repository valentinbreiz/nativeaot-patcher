﻿using Cosmos.API.Attributes;
using Cosmos.NativeWrapper;
using Mono.Cecil;
using Mono.Collections.Generic;

namespace Cosmos.Patcher.Tests;

public class PlugScannerTests_LoadPlugs
{
    private AssemblyDefinition CreateMockAssembly<T>()
    {
        string? assemblyPath = typeof(T).Assembly.Location;
        return AssemblyDefinition.ReadAssembly(assemblyPath);
    }

    [Fact]
    public void LoadPlugs_ShouldFindPluggedClasses()
    {
        // Arrange
        AssemblyDefinition? assembly = CreateMockAssembly<MockPlug>();
        PlugScanner? scanner = new();

        // Act
        List<TypeDefinition>? plugs = scanner.LoadPlugs(assembly);

        // Assert
        Assert.Contains(plugs, plug => plug.Name == nameof(MockPlug));
        TypeDefinition? plug = plugs.FirstOrDefault(p => p.Name == nameof(MockPlug));
        Assert.NotNull(plug);

        Collection<CustomAttribute>? customAttributes = plug.CustomAttributes;
        Assert.Contains(customAttributes, attr => attr.AttributeType.FullName == typeof(PlugAttribute).FullName);
        CustomAttribute? plugAttribute =
            customAttributes.First(attr => attr.AttributeType.FullName == typeof(PlugAttribute).FullName);

        string? expected = typeof(MockTarget).FullName;
        string? actual = plugAttribute.ConstructorArguments[0].Value?.ToString();
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void LoadPlugs_ShouldIgnoreClassesWithoutPlugAttribute()
    {
        // Arrange
        AssemblyDefinition? assembly = CreateMockAssembly<NonPlug>();
        PlugScanner? scanner = new();

        // Act
        List<TypeDefinition>? plugs = scanner.LoadPlugs(assembly);

        // Assert
        Assert.DoesNotContain(plugs, plug => plug.Name == nameof(NonPlug));
    }

    [Fact]
    public void LoadPlugs_ShouldHandleOptionalPlugs()
    {
        // Arrange
        AssemblyDefinition? assembly = CreateMockAssembly<OptionalPlug>();
        PlugScanner? scanner = new();

        // Act
        List<TypeDefinition>? plugs = scanner.LoadPlugs(assembly);
        TypeDefinition? optionalPlug = plugs.FirstOrDefault(p => p.Name == nameof(OptionalPlug));

        // Assert
        Assert.NotNull(optionalPlug);

        Collection<CustomAttribute>? customAttributes = optionalPlug.CustomAttributes;
        Assert.Contains(customAttributes, attr => attr.AttributeType.FullName == typeof(PlugAttribute).FullName);
        CustomAttribute? plugAttribute =
            customAttributes.First(attr => attr.AttributeType.FullName == typeof(PlugAttribute).FullName);
        Assert.Equal("OptionalTarget", plugAttribute.ConstructorArguments[0].Value);
        Assert.True((bool)plugAttribute.Properties.FirstOrDefault(p => p.Name == "IsOptional").Argument.Value);
    }
}
