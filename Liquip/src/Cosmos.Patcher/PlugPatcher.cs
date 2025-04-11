using Cosmos.API.Attributes;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Utils;

namespace Cosmos.Patcher;

/// <summary>
/// The PlugPatcher class is responsible for applying plugs to methods, types, and assemblies.
/// It allows the replacement of existing implementations with custom plugs, including support for instance methods using 'aThis'.
/// </summary>
public sealed class PlugPatcher
{
    private readonly PlugScanner _scanner;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlugPatcher"/> class.
    /// </summary>
    /// <param name="scanner">The plug scanner used to identify plugs in assemblies.</param>
    public PlugPatcher(PlugScanner scanner) => _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));

    /// <summary>
    /// Scans for plug methods and patches corresponding target methods in the target type.
    /// </summary>
    /// <param name="targetType">The target class to patch.</param>
    /// <param name="plugAssemblies">The assemblies to search for plug methods.</param>
    public void PatchType(TypeDefinition targetType, params AssemblyDefinition[] plugAssemblies)
    {
        ArgumentNullException.ThrowIfNull(targetType);
        if (plugAssemblies is null || plugAssemblies.Length == 0)
        {
            throw new ArgumentNullException(nameof(plugAssemblies));
        }

        // Ensure the target type is not a plug type itself
        if (targetType.CustomAttributes.Any(attr => attr.AttributeType.FullName == typeof(PlugAttribute).FullName))
        {
            Console.WriteLine($"Skipping patching of plug type: {targetType.FullName}");
            return;
        }

        Console.WriteLine($"Scanning and patching type: {targetType.FullName}");

        List<MethodDefinition> plugTypes = _scanner.LoadPlugs(plugAssemblies);
        foreach (MethodDefinition? plugType in plugTypes)
        {
            CustomAttribute? plugMethodAttribute = plugType.CustomAttributes
                .FirstOrDefault(attr =>
                    attr.AttributeType.FullName == typeof(PlugMethodAttribute).FullName ||
                    attr.AttributeType.FullName == typeof(NativeMethodAttribute).FullName);

            if (plugMethodAttribute is null)
                continue;

            bool isNativeMethod = plugMethodAttribute.AttributeType.FullName == typeof(NativeMethodAttribute).FullName;
            if (isNativeMethod)
                PatchNativeMethodType(plugType, plugMethodAttribute);
            else
                PatchPlugType(targetType, plugType, plugMethodAttribute);
        }
    }

    /// <summary>
    /// Patches an entire assembly by scanning for target types and their corresponding plugs.
    /// </summary>
    /// <param name="targetAssembly">The assembly to patch.</param>
    /// <param name="plugAssemblies">The assemblies to search for plug methods.</param>
    public void PatchAssembly(AssemblyDefinition targetAssembly, params AssemblyDefinition[] plugAssemblies)
    {
        ArgumentNullException.ThrowIfNull(targetAssembly);

        if (plugAssemblies is null || plugAssemblies.Length == 0)
            throw new ArgumentNullException(nameof(plugAssemblies));

        Console.WriteLine($"Scanning and patching assembly: {targetAssembly.MainModule.Name}");

        foreach (TypeDefinition? targetType in targetAssembly.MainModule.Types)
        {
            PatchType(targetType, plugAssemblies);
        }

        targetAssembly.UpdateFieldsPropertiesAndMethods(true);

        Console.WriteLine($"Patched assembly: {targetAssembly.MainModule.Name} successfully.");
    }


    private void PatchPlugType(TypeDefinition targetType, MethodDefinition plugMethod,
        CustomAttribute plugMethodAttribute)
    {
        string? targetTypeName = plugMethodAttribute.GetArgument<Type>([0, "TargetClass"]).FullName;

        if (string.IsNullOrEmpty(targetTypeName))
        {
            CustomAttribute? plugAttribute =
                plugMethod.DeclaringType.CustomAttributes.FirstOrDefault(attr =>
                    attr.AttributeType.FullName == typeof(PlugAttribute).FullName);

            if (plugAttribute is null)
                return;

            targetTypeName = plugAttribute.GetArgument<Type>([0, "Target"]).FullName ??
                             plugAttribute.GetArgument<string>([0, "TargetName"]);
        }

        if (targetTypeName != targetType.FullName)
            return;

        if (plugMethod.Name is "Ctor" or "CCtor")
        {
            bool isInstanceCtorPlug = plugMethod.Parameters.Any(p => p.Name == "aThis");
            MethodDefinition? targetConstructor = targetType.Methods.FirstOrDefault(m =>
                m.IsConstructor &&
                (isInstanceCtorPlug
                    ? m.Parameters.Count + 1 == plugMethod.Parameters.Count
                    : m.Parameters.Count == plugMethod.Parameters.Count) &&
                (plugMethod.Name != "CCtor" || m.IsStatic));

            if (targetConstructor is not null)
            {
                Console.WriteLine($"Found matching constructor: {targetConstructor.Name}. Patching...");
                PatchPlugMethod(targetConstructor, plugMethod);
            }
            else
            {
                Console.WriteLine($"No matching constructor found for plug: {plugMethod.Name}");
            }

            return;
        }

        bool isInstancePlug = plugMethod.Parameters.Any(p =>
            p.Name == "aThis" && p.ParameterType.FullName == "System.Object");

        MethodDefinition? targetMethod = targetType.Methods.FirstOrDefault(m => m.Name == plugMethod.Name &&
            (isInstancePlug
                ? m.Parameters.Count + 1 == plugMethod.Parameters.Count
                : m.Parameters.Count == plugMethod.Parameters.Count));

        if (targetMethod is not null)
        {
            Console.WriteLine($"Found matching method: {targetMethod.Name}. Patching...");
            PatchPlugMethod(targetMethod, plugMethod);
        }
        else
        {
            Console.WriteLine($"No matching method found for plug: {plugMethod.Name}");
        }
    }


    private void PatchNativeMethodType(MethodDefinition plugMethod,
        CustomAttribute nativeMethodAttribute)
    {
        PatchNativeMethod(plugMethod, nativeMethodAttribute);
    }

    /// <summary>
    /// Patches the target method by replacing its body with the body of the plug method.
    /// </summary>
    /// <param name="targetMethod">The method in the base class to patch.</param>
    /// <param name="plugMethod">The plug method providing the replacement body.</param>
    public void PatchPlugMethod(MethodDefinition targetMethod, MethodDefinition plugMethod)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);
        ArgumentNullException.ThrowIfNull(plugMethod);

        Console.WriteLine($"Patching method: {targetMethod.FullName} with plug: {plugMethod.FullName}");

        targetMethod.Body ??= new MethodBody(targetMethod);
        ILProcessor processor = targetMethod.Body.GetILProcessor();

        // When patching a constructor, preserve field init and base call
        if (targetMethod.IsConstructor)
        {
            Instruction? firstNopInstruction =
                targetMethod.Body.Instructions.FirstOrDefault(instr => instr.OpCode == OpCodes.Nop);
            if (firstNopInstruction is not null)
            {
                int firstNopIndex = targetMethod.Body.Instructions.IndexOf(firstNopInstruction);

                // Remove all instructions after the first Nop
                for (int i = targetMethod.Body.Instructions.Count - 1; i > firstNopIndex; i--)
                {
                    processor.Remove(targetMethod.Body.Instructions[i]);
                }
            }
        }
        else
        {
            targetMethod.Body.Instructions.Clear();
        }

        bool isInstancePlug =
            plugMethod.Parameters.Any(p => p.Name == "aThis" && p.ParameterType.FullName == "System.Object");

        if ((isInstancePlug && !targetMethod.IsStatic) || targetMethod.IsConstructor)
        {
            foreach (Instruction instruction in plugMethod.Body.Instructions)
            {
                Instruction cloned = instruction.Clone();
                cloned.Operand = cloned.Operand switch
                {
                    MethodReference m => targetMethod.Module.ImportReference(m),
                    FieldReference f => targetMethod.Module.ImportReference(f),
                    TypeReference t => targetMethod.Module.ImportReference(t),
                    MemberReference mr => targetMethod.Module.ImportReference(mr),
                    _ => cloned.Operand
                };

                processor.Append(cloned);
            }
        }
        else
        {
            targetMethod.SwapMethods(plugMethod);
        }

        if (targetMethod.Body.Instructions.Count == 0 || targetMethod.Body.Instructions[^1].OpCode != OpCodes.Ret)
        {
            processor.Append(Instruction.Create(OpCodes.Ret));
        }

        Console.WriteLine($"Patched method: {targetMethod.Name} successfully.");
    }


    public void PatchNativeMethod(MethodDefinition plugMethod, CustomAttribute nativeMethodAttribute)
    {
        ModuleDefinition module = plugMethod.Module;
        MethodDefinition nativeMethod = new($"{plugMethod.Name}_Native",
            MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.PInvokeImpl,
            plugMethod.ReturnType) { ImplAttributes = MethodImplAttributes.InternalCall };

        var runtimeImportAttrRef = new TypeReference(
            "System.Runtime",
            "RuntimeImportAttribute",
            module,
            module.TypeSystem.CoreLibrary
        );

        var runtimeImportCtor = new MethodReference(".ctor", module.TypeSystem.Void, runtimeImportAttrRef)
        {
            HasThis = true,
        };

        runtimeImportCtor = module.ImportReference(runtimeImportCtor);
        var runtimeImportAttr = new CustomAttribute(runtimeImportCtor);

        runtimeImportAttr.ConstructorArguments.Add(new CustomAttributeArgument(module.TypeSystem.String,nativeMethodAttribute.GetArgument<string>([0,"SymbolDll"])));

        plugMethod.Attributes = plugMethod.Attributes &= ~(MethodAttributes.PInvokeImpl | MethodAttributes.Static);

        plugMethod.Body = new MethodBody(plugMethod);
        ILProcessor processor = plugMethod.Body.GetILProcessor();
        processor.Append(Instruction.Create(OpCodes.Call, nativeMethod));
        processor.Append(Instruction.Create(OpCodes.Ret));
    }
}
