using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cosmos.API.Attributes;

namespace Cosmos.Entry
{
    public static class Program
    {
        [UnmanagedCallersOnly(EntryPoint = "dotnet_main", CallConvs = [typeof(CallConvCdecl)])]
        public static void Main()
        {
            
        }
    }
}
