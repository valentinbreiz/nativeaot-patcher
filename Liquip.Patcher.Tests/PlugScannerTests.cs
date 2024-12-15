using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Liquip.API.Attributes;
using Xunit;
using Liquip.Patcher;

namespace Liquip.Patcher.Tests
{
    public class MockTarget { }

    public class NonPlug { }

    [Plug(typeof(MockTarget))]
    public class MockPlug { }

    [Plug(typeof(MockTarget))]
    public class EmptyPlug
    {
        // No methods defined
    }

    [Plug(typeof(MockTarget))]
    public class MockPlugWithMethods
    {
        public static void StaticMethod() { }
        public void InstanceMethod() { }
    }

    [Plug("OptionalTarget", IsOptional = true)]
    public class OptionalPlug { }
}