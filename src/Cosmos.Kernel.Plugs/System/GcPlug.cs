// This code is licensed under MIT license (see LICENSE for details)

using System;
using System.Collections.Generic;
using System.Text;
using Cosmos.Build.API.Attributes;
using Cosmos.Kernel.Core.Memory.GarbageCollector;
using Cosmos.Kernel.Core.Memory.Heap;

namespace Cosmos.Kernel.Plugs.System;

[Plug(typeof(GC))]
public static class GcPlug
{
    [PlugMember("GetConfigurationVariables")]
    public static IReadOnlyDictionary<string, object> GetConfigurationVariables()
    {
        return GarbageCollector.Variables;
    }

}
