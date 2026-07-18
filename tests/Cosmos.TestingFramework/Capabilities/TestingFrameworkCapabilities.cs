using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Testing.Platform.Capabilities.TestFramework;

namespace Cosmos.TestingFramework.Capabilities
{
    internal sealed class TestingFrameworkCapabilities : ITestFrameworkCapabilities
    {
        public TrxCapability TrxCapability { get; } = new();

        public IReadOnlyCollection<ITestFrameworkCapability> Capabilities => [TrxCapability];
    }
}
