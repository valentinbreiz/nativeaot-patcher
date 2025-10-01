using System;

namespace Cosmos.Tests.Kernel.Framework;

/// <summary>
/// Marks a method as a test method that should be executed by the test runner.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class TestAttribute : Attribute
{
    public string? Description { get; set; }
}
