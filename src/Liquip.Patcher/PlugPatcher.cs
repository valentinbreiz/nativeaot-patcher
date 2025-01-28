using Liquip.API.Attributes;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Utils;

namespace Liquip.Patcher;

/// <summary>
/// The PlugPatcher class is responsible for applying plugs to methods, types, and assemblies.
/// It allows the replacement of existing implementations with custom plugs, including support for instance methods using 'aThis'.
/// </summary>
public class PlugPatcher
{
    private readonly PlugScanner _scanner;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlugPatcher"/> class.
    /// </summary>
    /// <param name="scanner">The plug scanner used to identify plugs in assemblies.</param>
    public PlugPatcher(PlugScanner scanner) => _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));

    /// <summary>
    /// Patches the target method by replacing its body with the body of the plug method.
    /// </summary>
    /// <param name="targetMethod">The method in the base class to patch.</param>
    /// <param name="plugMethod">The plug method providing the replacement body.</param>
    public void PatchMethod(MethodDefinition targetMethod, MethodDefinition plugMethod)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);
        ArgumentNullException.ThrowIfNull(plugMethod);

        Console.WriteLine($"Patching method: {targetMethod.FullName} with plug: {plugMethod.FullName}");

        targetMethod.Body ??= new MethodBody(targetMethod);

        ILProcessor? processor = targetMethod.Body.GetILProcessor();

        // When patching a constructor, we need to leave the field initialization of the target constructor intact
        // TODO: Decide if we want to keep this part of the constructor or remove it (maybe with an attribute on the plug)
        if (targetMethod.IsConstructor)
        {
            // Clear everything except field initialization and base class constructor call
            Instruction? firstNopInstruction = targetMethod.Body.Instructions.FirstOrDefault(instr => instr.OpCode == OpCodes.Nop);
            if (firstNopInstruction is not null)
            {
                int firstNopIndex = targetMethod.Body.Instructions.IndexOf(firstNopInstruction);

                // Remove all instructions after the first Nop instruction
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

        // If the plug method is an instance method or constructor, we need to clone/append the instructions, otherwise we can swap the methods
        if ((isInstancePlug && !targetMethod.IsStatic) || targetMethod.IsConstructor)
        {
            foreach (Instruction? instruction in plugMethod.Body.Instructions)
            {
                Instruction? clonedInstruction = instruction.Clone();

                clonedInstruction.Operand = clonedInstruction.Operand switch
                {
                    MethodReference methodRef => targetMethod.Module.ImportReference(methodRef),
                    FieldReference fieldRef => targetMethod.Module.ImportReference(fieldRef),
                    TypeReference typeRef => targetMethod.Module.ImportReference(typeRef),
                    MemberReference memberRef => targetMethod.Module.ImportReference(memberRef),
                    _ => clonedInstruction.Operand
                };

                processor.Append(clonedInstruction);
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

        List<TypeDefinition>? plugTypes = _scanner.LoadPlugs(plugAssemblies);

        if (plugTypes is not null)
        {
            foreach (TypeDefinition? plugType in plugTypes)
            {
                CustomAttribute? plugAttribute = plugType.CustomAttributes
                    .FirstOrDefault(attr => attr.AttributeType.FullName == typeof(PlugAttribute).FullName);

                if (plugAttribute is null)
                    continue;


                string? targetTypeName = plugAttribute.ConstructorArguments.FirstOrDefault().Value?.ToString();
                if (targetTypeName != targetType.FullName)
                    continue;

                foreach (MethodDefinition? plugMethod in plugType.Methods.Where(m => !m.IsConstructor))
                {
                    if (plugMethod.Name == "Ctor" || plugMethod.Name == "Cctor")
                    {
                        bool isInstanceCtorPlug = plugMethod.Parameters.Any(p => p.Name == "aThis");

                        MethodDefinition? targetConstructor = targetType.Methods.FirstOrDefault(m =>
                            m.IsConstructor &&
                            (isInstanceCtorPlug
                                ? m.Parameters.Count + 1 == plugMethod.Parameters.Count
                                : m.Parameters.Count == plugMethod.Parameters.Count) &&
                            (plugMethod.Name != "Cctor" || m.IsStatic));

                        if (targetConstructor is not null)
                        {
                            Console.WriteLine($"Found matching constructor: {targetConstructor.Name}. Patching...");
                            PatchMethod(targetConstructor, plugMethod);
                        }
                        else
                        {
                            Console.WriteLine($"No matching constructor found for plug: {plugMethod.Name}");
                        }

                        continue;
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
                        PatchMethod(targetMethod, plugMethod);
                    }
                    else
                    {
                        Console.WriteLine($"No matching method found for plug: {plugMethod.Name}");
                    }
                }
            }
        }

        Console.WriteLine($"Patched type: {targetType.Name} successfully.");
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
        {
            throw new ArgumentNullException(nameof(plugAssemblies));
        }

        Console.WriteLine($"Scanning and patching assembly: {targetAssembly.MainModule.Name}");

        foreach (TypeDefinition? targetType in targetAssembly.MainModule.Types)
        {
            PatchType(targetType, plugAssemblies);
        }

        targetAssembly.UpdateFieldsPropertiesAndMethods(true);

        Console.WriteLine($"Patched assembly: {targetAssembly.MainModule.Name} successfully.");
    }
}
