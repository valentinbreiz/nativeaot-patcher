using System.Diagnostics.CodeAnalysis;
using Liquip.Patcher.Extensions;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Utils;

namespace Liquip.Patcher;

public static class PlugPatcher
{
    public static void PatchMethod(MethodDefinition org, MethodDefinition newMethodBody)
    {
        org.Body = newMethodBody.Body.ReplaceMethodBody(org);
    }
    
    
    
}