using Cosmos.API.Attributes;
using Cosmos.API.Enum;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Utils;

namespace Cosmos.Patcher;

/// <summary>
/// The PlugPatcher class is responsible for applying plugs to methods, types, and assemblies.
/// </summary>
public sealed class PlugPatcher
{
    private readonly PlugScanner _scanner;

    public PlugPatcher(PlugScanner scanner)
    {
        Console.WriteLine("[Init] Initializing PlugPatcher...");
        _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
        Console.WriteLine("[Init] PlugScanner initialized successfully.");
    }

    public void PatchAssembly(AssemblyDefinition targetAssembly, params AssemblyDefinition[] plugAssemblies)
    {
        Console.WriteLine($"[PatchAssembly] Patching assembly: {targetAssembly.FullName}");

        foreach (TypeDefinition? type in targetAssembly.MainModule.Types)
        {
            Console.WriteLine($"[PatchAssembly] Patching type: {type.FullName}");
            PatchType(type, plugAssemblies);
        }

        Console.WriteLine("[PatchAssembly] Updating fields, properties, and methods...");
        targetAssembly.UpdateFieldsPropertiesAndMethods(true);

        Console.WriteLine($"[PatchAssembly] Finished patching assembly: {targetAssembly.Name}");
    }

    public void PatchType(TypeDefinition targetType, params AssemblyDefinition[] plugAssemblies)
    {
        Console.WriteLine($"[PatchType] Called for type: {targetType.FullName}");

        if (plugAssemblies is null || plugAssemblies.Length == 0)
        {
            Console.WriteLine("[PatchType] No plug assemblies provided.");
            throw new ArgumentNullException(nameof(plugAssemblies));
        }

        if (targetType.CustomAttributes.Any(attr => attr.AttributeType.FullName == typeof(PlugAttribute).FullName))
        {
            Console.WriteLine($"[PatchType] Skipping plug type: {targetType.FullName}");
            return;
        }

        Console.WriteLine($"[PatchType] Scanning plug methods in assemblies...");
        List<TypeDefinition> plugs = _scanner.LoadPlugs(plugAssemblies);

        Console.WriteLine($"[PatchType] Found {plugs.Count} plugs. Beginning patching...");
        foreach (TypeDefinition plugType in plugs)
        {
            Console.WriteLine($"[PatchType] Processing plug method: {plugType.FullName}");
            CustomAttribute? plugAttr = plugType.CustomAttributes
                .FirstOrDefault(a => a.AttributeType.FullName == typeof(PlugAttribute).FullName);


            if (plugAttr is null)
            {
                Console.WriteLine("[PatchType] Skipping plug without PlugMethod or NativeMethod attribute.");
                continue;
            }

            string? targetTypeName = plugAttr.GetArgument<string>(fallbackArgs: [0, "Target"]);
            if (string.IsNullOrEmpty(targetTypeName))
                targetTypeName = plugAttr.GetArgument<string>(fallbackArgs: [0, "TargetName"]);

            if (targetTypeName != targetType.FullName)
                continue;

            foreach (MethodDefinition? method in plugType.Methods.Where(m => !m.IsConstructor))
            {
                CustomAttribute? plugMemberAttr = method.CustomAttributes.FirstOrDefault(attr=>attr.AttributeType.FullName == typeof(PlugMemberAttribute).FullName);
                if (plugMemberAttr == null)
                     continue;

                PatchPlugMethod(targetType, method, plugMemberAttr);
            }
        }

        Console.WriteLine($"[PatchType] Done patching type: {targetType.FullName}");
    }




    private void PatchPlugMethod(TypeDefinition targetType, MethodDefinition plugMethod, CustomAttribute attr)
    {
        Console.WriteLine($"[PatchPlugType] Patching plug method: {plugMethod.FullName}");

        if (plugMethod.Name is "Ctor" or "CCtor")
        {
            bool isInstanceCtor = plugMethod.Parameters.Any(p => p.Name == "aThis");
            Console.WriteLine($"[PatchPlugType] Detected constructor plug. Instance: {isInstanceCtor}");

            MethodDefinition? ctor = targetType.Methods.FirstOrDefault(m =>
                m.IsConstructor &&
                (isInstanceCtor
                    ? m.Parameters.Count + 1 == plugMethod.Parameters.Count
                    : m.Parameters.Count == plugMethod.Parameters.Count) &&
                (plugMethod.Name != "CCtor" || m.IsStatic));

            if (ctor != null)
            {
                Console.WriteLine($"[PatchPlugType] Found constructor: {ctor.Name}");
                PatchMethod(ctor, plugMethod);
            }
            else
            {
                Console.WriteLine("[PatchPlugType] No matching constructor found.");
            }

            return;
        }

        bool isInstancePlug = plugMethod.Parameters.Any(p => p.Name == "aThis");

        string methodName = attr.GetArgument(plugMethod.Name, 0, "TargetName")!;
        Console.WriteLine($"[PatchPlugType] Looking for target method: {methodName}");

        MethodDefinition? targetMethod = targetType.Methods.FirstOrDefault(m => m.Name == methodName &&
            (isInstancePlug
                ? m.Parameters.Count + 1 == plugMethod.Parameters.Count
                : m.Parameters.Count == plugMethod.Parameters.Count));

        if (targetMethod is not null)
        {
            Console.WriteLine($"[PatchPlugType] Found method: {targetMethod.Name}");
            PatchMethod(targetMethod, plugMethod);
        }
        else
        {
            Console.WriteLine("[PatchPlugType] No matching method found.");
        }
    }

    public void PatchMethod(MethodDefinition targetMethod, MethodDefinition plugMethod)
    {
        Console.WriteLine($"[PatchMethod] Patching {targetMethod.FullName} with {plugMethod.FullName}");

        targetMethod.Body ??= new MethodBody(targetMethod);
        ILProcessor processor = targetMethod.Body.GetILProcessor();

        if (targetMethod.IsConstructor)
        {
            Instruction? nop = targetMethod.Body.Instructions.FirstOrDefault(i => i.OpCode == OpCodes.Nop);
            if (nop is not null)
            {
                int index = targetMethod.Body.Instructions.IndexOf(nop);
                Console.WriteLine($"[PatchMethod] Constructor: preserving up to index {index} (NOP)");
                for (int i = targetMethod.Body.Instructions.Count - 1; i > index; i--)
                {
                    processor.Remove(targetMethod.Body.Instructions[i]);
                }
            }
        }
        else
        {
            targetMethod.Body.Instructions.Clear();
        }

        bool isInstance = plugMethod.Parameters.Any(p => p.Name == "aThis");

        if ((isInstance && !targetMethod.IsStatic) || targetMethod.IsConstructor)
        {
            foreach (Instruction? instruction in plugMethod.Body.Instructions)
            {
                Instruction clone = instruction.Clone();
                clone.Operand = clone.Operand switch
                {
                    MethodReference m => targetMethod.Module.ImportReference(m),
                    FieldReference f => targetMethod.Module.ImportReference(f),
                    TypeReference t => targetMethod.Module.ImportReference(t),
                    MemberReference mr => targetMethod.Module.ImportReference(mr),
                    _ => clone.Operand
                };
                processor.Append(clone);
            }
        }
        else
        {
            Console.WriteLine("[PatchMethod] Swapping method body entirely.");
            targetMethod.SwapMethods(plugMethod);
        }

        if (targetMethod.Body.Instructions.Count == 0 || targetMethod.Body.Instructions[^1].OpCode != OpCodes.Ret)
        {
            Console.WriteLine("[PatchMethod] Appending return instruction.");
            processor.Append(Instruction.Create(OpCodes.Ret));
        }

        Console.WriteLine($"[PatchMethod] Successfully patched: {targetMethod.FullName}");
    }

    public void PatchProperty(PropertyDefinition targetProperty, PropertyDefinition plugProperty)
    {

    }

    public void PatchField(FieldDefinition targetField, FieldDefinition plugField)
    {
    }
}
