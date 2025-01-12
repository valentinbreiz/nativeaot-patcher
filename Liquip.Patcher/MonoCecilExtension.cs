#if (UNITY_2017_1_OR_NEWER && UNITY_EDITOR) || !UNITY_2017_1_OR_NEWER
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;
using MethodBody = Mono.Cecil.Cil.MethodBody;

/// <summary>
/// Provides extension methods for classes from the Mono.Cecil library, a library for reading and writing Intermediate Language (IL) code.
/// These extensions facilitate manipulation of IL code, providing functionality to clone, merge, and update types in collections, methods, fields, and other components of a .NET assembly.
/// </summary>
public static class MonoCecilExtensions
{
    /// <summary>
    /// Represents an information container for updating Mono.Cecil definitions.
    /// </summary>
    public class UpdateInfo
    {
        /// <summary>
        /// A collection of CustomAttribute objects that have been updated.
        /// </summary>
        public readonly Collection<CustomAttribute> updatedAttributes = [];

        /// <summary>
        /// A collection of InterfaceImplementation objects that have been updated.
        /// </summary>
        public readonly Collection<InterfaceImplementation> updatedInterfaces = [];

        /// <summary>
        /// A collection of FieldDefinition objects that have been updated.
        /// </summary>
        public readonly Collection<FieldDefinition> updatedFields = [];

        /// <summary>
        /// A collection of PropertyDefinition objects that have been updated.
        /// </summary>
        public readonly Collection<PropertyDefinition> updatedProperties = [];

        /// <summary>
        /// A collection of MethodDefinition objects that have been updated.
        /// </summary>
        public readonly Collection<MethodDefinition> updatedMethods = [];

        /// <summary>
        /// A collection of source TypeDefinition objects that are being merged.
        /// </summary>
        public readonly Collection<TypeDefinition> srcTypes = [];

        /// <summary>
        /// A collection of destination TypeDefinition objects where source objects are merged into.
        /// </summary>
        public readonly Collection<TypeDefinition> destTypes = [];
    };

    /// <summary>
    /// A dictionary mapping from AssemblyDefinition objects to their corresponding UpdateInfo objects.
    /// Used to keep track of the updates made to each assembly.
    /// </summary>
    public static readonly Dictionary<AssemblyDefinition, UpdateInfo> assemblyUpdateInfo = [];

    /// <summary>
    /// Additional search directories for resolving assembly types.
    /// </summary>
    public static readonly Collection<string> additionalSearchDirectories = [];

    // Basic extension methods for loading assemblies, adding elements to collections, and finding types, fields, and methods in Mono.Cecil objects.

    #region Base

    /// <summary>
    /// This extension method loads an assembly from a given location.
    /// </summary>
    /// <param name="location">The location of the assembly to be loaded.</param>
    /// <param name="readWrite">A boolean value to determine if the assembly is read-only or writable.</param>
    /// <returns>The AssemblyDefinition object of the loaded assembly if successful</returns>
    public static AssemblyDefinition LoadAssembly(this string location, bool readWrite = false)
    {
        // Create a new instance of the DefaultAssemblyResolver.
        DefaultAssemblyResolver? resolver = new();

        // Add search directories to the resolver.
        foreach (string? directory in additionalSearchDirectories)
        {
            resolver.AddSearchDirectory(directory);
        }

        resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(int).Assembly.Location));
        resolver.AddSearchDirectory(Path.Combine(Path.GetDirectoryName(typeof(int).Assembly.Location), "Facades"));

        // Read and return the assembly using the provided location and reader parameters.
        return AssemblyDefinition.ReadAssembly(location,
            new ReaderParameters() { AssemblyResolver = resolver, ReadWrite = readWrite });
    }

    /// <summary>
    /// This extension method finds a method of a given type in an assembly.
    /// </summary>
    /// <param name="assembly">The assembly where the type and method are located.</param>
    /// <param name="typeSignature">The full or simple name of the type.</param>
    /// <param name="methodSignature">The full or simple name of the method.</param>
    /// <returns>The MethodDefinition object of the found method. Null if not found.</returns>
    public static MethodDefinition FindMethodOfType(this AssemblyDefinition assembly, string typeSignature,
        string methodSignature) =>
        // Find and return the method of the given type in the assembly.
        assembly.FindType(typeSignature)?.FindMethod(methodSignature);

    /// <summary>
    /// This extension method finds a type in an assembly using its full name or simple name.
    /// </summary>
    /// <param name="assembly">The assembly where the type is located.</param>
    /// <param name="typeSignature">The full or simple name of the type.</param>
    /// <returns>The TypeDefinition object of the found type. Null if not found.</returns>
    public static TypeDefinition FindType(this AssemblyDefinition assembly, string typeSignature) =>
        // Return the first type that matches the provided type signature.
        assembly.MainModule.Types.FirstOrDefault(type => type.FullName == typeSignature || type.Name == typeSignature);

    /// <summary>
    /// This extension method finds a type in an assembly using its full name or simple name.
    /// </summary>
    /// <param name="assembly">The assembly where the type is located.</param>
    /// <param name="type">The type to locate.</param>
    /// <returns>The TypeDefinition object of the found type. Null if not found.</returns>
    public static TypeDefinition FindType(this AssemblyDefinition assembly, Type type) =>
        // Return the first type that matches the provided type signature.
        assembly.MainModule.Types.FirstOrDefault(_type => _type.FullName == type.FullName || _type.Name == type.Name);

    /// <summary>
    /// This extension method finds a field in a type.
    /// </summary>
    /// <param name="type">The type where the field is located.</param>
    /// <param name="fieldSignature">The full or simple name of the field.</param>
    /// <returns>The FieldDefinition object of the found field. Null if not found.</returns>
    public static FieldDefinition FindField(this TypeDefinition type, string fieldSignature) =>
        // Return the first field that matches the provided field signature.
        type.Fields.FirstOrDefault(m => m.FullName == fieldSignature || m.Name == fieldSignature);

    /// <summary>
    /// This extension method finds a method in a type.
    /// </summary>
    /// <param name="type">The type where the method is located.</param>
    /// <param name="methodSignature">The full or simple name of the method.</param>
    /// <returns>The MethodDefinition object of the found method. Null if not found.</returns>
    public static MethodDefinition FindMethod(this TypeDefinition type, string methodSignature) =>
        // The function checks each method in the type's Methods collection,
        // and returns the first method whose full name or simple name matches the provided method signature.
        type.Methods.FirstOrDefault(m => m.FullName == methodSignature || m.Name == methodSignature);

    /// <summary>
    /// This extension method finds all methods in a type that match a given method signature.
    /// </summary>
    /// <param name="type">The type where the methods are located.</param>
    /// <param name="methodSignature">The full or simple name of the methods.</param>
    /// <returns>A collection of MethodDefinition objects for the found methods. Empty collection if none found.</returns>
    public static Collection<MethodDefinition> FindMethods(this TypeDefinition type, string methodSignature)
    {
        Collection<MethodDefinition>? collection = new();

        // This function checks each method in the type's Methods collection,
        // and adds those methods to the collection whose full name or simple name matches the provided method signature.
        foreach (MethodDefinition? item in type.Methods.Where(m =>
                     m.FullName == methodSignature || m.Name == methodSignature))
        {
            collection.Add(item);
        }

        return collection;
    }

    #endregion Base

    // Extension method that handles adding types to an assembly.

    #region AddType

    /// <summary>
    /// Adds a type to an assembly. This includes adding the type's fields, properties, and methods.
    /// If the source type is nested, it will be added as a nested type within the parent type in the destination assembly.
    /// </summary>
    /// <param name="assembly">The assembly to which the type will be added.</param>
    /// <param name="src">The source type that will be added to the assembly.</param>
    /// <param name="avoidSignatureConflicts">Avoid name conflicts by adding a '_' suffix to the copied class name.</param>
    public static void AddType(this AssemblyDefinition assembly, TypeDefinition src,
        bool avoidSignatureConflicts = false)
    {
        // Check for signature conflict avoidance
        string? srcName = src.Name;
        if (avoidSignatureConflicts)
        {
            src.Name += "_";
        }

        // Create a new TypeDefinition with the same properties as the source type
        TypeDefinition? dest = new(src.Namespace, src.Name, src.Attributes);

        // If the source type isn't nested, add the new type directly to the assembly's types
        // Otherwise, find the declaring type in the assembly and add the new type as a nested type
        if (!src.IsNested)
        {
            assembly.MainModule.Types.Add(dest);
        }
        else
        {
            assembly.FindType(src.DeclaringType.FullName).NestedTypes.Add(dest);
        }

        // Set the base type of the new type to match the base type of the source type
        dest.BaseType = src.BaseType;

        // Add the fields, properties, and methods from the source type to the new type
        dest.AddFieldsPropertiesAndMethods(src);

        // Restore name
        if (avoidSignatureConflicts)
        {
            src.Name = srcName;
        }
    }

    #endregion AddType

    // Extension method that handles the addition of fields, properties, and methods from a source type to a destination type.
    // This is a key part of merging two types, ensuring the destination type includes all necessary components from the source type.

    #region AddFieldsPropertiesAndMethods

    /// <summary>
    /// Merges the source type into the destination type by cloning the fields, properties, and methods of the source, updating their types and adding them to the destination.
    /// </summary>
    /// <param name="dest">The destination type definition where fields, properties, and methods from source will be added.</param>
    /// <param name="src">The source type definition whose fields, properties, and methods will be cloned and added to the destination.</param>
    public static void AddFieldsPropertiesAndMethods(this TypeDefinition dest, TypeDefinition src)
    {
        // Add nested types to the module
        foreach (TypeDefinition? subtype in src.NestedTypes)
        {
            dest.Module.Assembly.AddType(subtype);
        }

        // Clone attributes from the source and add to the destination
        Collection<CustomAttribute>? clonedAttributes = new();
        foreach (CustomAttribute? attribute in src.CustomAttributes)
        {
            CustomAttribute? clonedAttribute = attribute.Clone();
            dest.CustomAttributes.Add(clonedAttribute);
            clonedAttributes.Add(clonedAttribute);
        }

        // Clone interfaces from the source and add to the destination
        Collection<InterfaceImplementation>? clonedInterfaces = new();
        foreach (InterfaceImplementation? @interface in src.Interfaces)
        {
            InterfaceImplementation? clonedInterface = @interface.Clone();
            dest.Interfaces.Add(clonedInterface);
            clonedInterfaces.Add(clonedInterface);
        }

        // Clone fields from the source and add to the destination
        Collection<FieldDefinition>? clonedFields = new();
        foreach (FieldDefinition? field in src.Fields)
        {
            FieldDefinition? clonedField = field.Clone();
            clonedFields.Add(clonedField);
            dest.Fields.Add(clonedField);
        }

        // Clone properties from the source and add to the destination
        Collection<PropertyDefinition>? clonedProperties = new();
        foreach (PropertyDefinition? property in src.Properties)
        {
            PropertyDefinition? clonedProperty = property.Clone();
            clonedProperties.Add(clonedProperty);
            dest.Properties.Add(clonedProperty);
        }

        // Clone methods from the source (don't add to the destination yet)
        Collection<MethodDefinition>? clonedMethods = new();
        foreach (MethodDefinition? method in src.Methods)
        {
            MethodDefinition? clonedMethod = method.Clone();
            clonedMethods.Add(clonedMethod);
        }

        // List for keeping track of methods that need further processing
        Collection<MethodDefinition>? updatedMethods = new();

        // Process each method
        foreach (MethodDefinition? clonedMethod in clonedMethods.ToList())
        {
            // Special handling for constructors
            if (clonedMethod.Name is ".ctor" or ".cctor" or "Finalize")
            {
                // Temporarily set the declaring type of the cloned method to the destination type
                // This is required to get the correct full name of the method for the FindMethod call
                clonedMethod.DeclaringType = dest;

                // Find an existing method in the destination type that matches the full name of the cloned method
                // Note that the full name of a method includes the name of its declaring type
                MethodDefinition? destMethod = dest.FindMethod(clonedMethod.FullName);

                // Reset the declaring type of the cloned method to null
                // This is done because the cloned method hasn't been added to the destination type yet,
                // and leaving the declaring type set will cause failures to add the method to the destination type
                clonedMethod.DeclaringType = null;

                // If destination already contains a constructor/destructor, merge the instructions
                if (destMethod != null)
                {
                    Collection<Instruction>? clonedInstructions = clonedMethod.Body.Instructions;
                    List<Instruction>? trimmedClonedInstructions = clonedInstructions.ToList();

                    // For constructors
                    if (clonedMethod.Name is ".ctor")
                    {
                        // Find the constructor call instruction and remove the instructions before it
                        // This is done to prevent calling the base class constructor twice when merging
                        int callIndex = trimmedClonedInstructions.FindIndex(x => x.OpCode == OpCodes.Call);

                        // Check if callIndex is within valid range
                        if (callIndex < 0 || callIndex >= trimmedClonedInstructions.Count)
                        {
                            throw new Exception("Invalid Call Instruction Index in cloned method.");
                        }

                        // Remove starting instructions
                        trimmedClonedInstructions.RemoveRange(0, callIndex + 1);
                        trimmedClonedInstructions.RemoveAt(trimmedClonedInstructions.Count - 1);

                        // Insert the trimmed instructions to the existing constructor, just before the last instruction (ret)
                        int insertIndex = destMethod.Body.Instructions.Count - 1;
                        foreach (Instruction? clonedInstruction in trimmedClonedInstructions)
                        {
                            destMethod.Body.Instructions.Insert(insertIndex, clonedInstruction);
                            insertIndex++;
                        }
                    }
                    // For static constructors
                    else if (clonedMethod.Name is ".cctor")
                    {
                        // Remove the last instruction (ret)
                        trimmedClonedInstructions.RemoveAt(trimmedClonedInstructions.Count - 1);

                        // Insert the trimmed instructions to the existing static constructor, just before the last instruction (ret)
                        int insertIndex = destMethod.Body.Instructions.Count - 1;
                        foreach (Instruction? clonedInstruction in trimmedClonedInstructions)
                        {
                            destMethod.Body.Instructions.Insert(insertIndex, clonedInstruction);
                            insertIndex++;
                        }
                    }
                    // For destructors
                    else if (clonedMethod.Name is "Finalize")
                    {
                        // Find the leave.s instruction and remove the instructions after it.
                        // This is done to prevent calling the base class destructor twice when merging.
                        int trimIndex = trimmedClonedInstructions.FindIndex(x => x.OpCode == OpCodes.Leave_S);

                        // Check if trimIndex is within valid range
                        if (trimIndex < 0 || trimIndex >= trimmedClonedInstructions.Count)
                        {
                            throw new Exception("Invalid trim index in cloned method.");
                        }

                        // Remove instructions after leave.s (inclusive)
                        trimmedClonedInstructions.RemoveRange(trimIndex, trimmedClonedInstructions.Count - trimIndex);

                        // Insert the trimmed instructions to the existing destructor, at the beginning
                        int insertionIndex = 0;
                        foreach (Instruction? clonedInstruction in trimmedClonedInstructions)
                        {
                            destMethod.Body.Instructions.Insert(insertionIndex, clonedInstruction);
                            insertionIndex++;
                        }
                    }

                    // Remove the cloned constructor or destructor from the list of methods to add to the destination type
                    _ = clonedMethods.Remove(clonedMethod);

                    // Add the method to the list of methods to update since it has been modified
                    updatedMethods.Add(destMethod);
                }
                else
                {
                    // Add the cloned constructor to the destination type
                    updatedMethods.Add(clonedMethod);
                }
            }
            else
            {
                // For non-constructor/non-destructor methods
                updatedMethods.Add(clonedMethod);
            }
        }

        // Add updated methods to the destination type
        foreach (MethodDefinition? method in clonedMethods)
        {
            dest.Methods.Add(method);
        }

        // Add updated attributes, interfaces, fields, properties and methods to the update info
        if (!assemblyUpdateInfo.TryGetValue(dest.Module.Assembly, out UpdateInfo? updateInfo))
        {
            updateInfo = assemblyUpdateInfo[dest.Module.Assembly] = new UpdateInfo();
        }

        foreach (CustomAttribute? attribute in clonedAttributes)
        {
            updateInfo.updatedAttributes.Add(attribute);
        }

        foreach (InterfaceImplementation? @interface in clonedInterfaces)
        {
            updateInfo.updatedInterfaces.Add(@interface);
        }

        foreach (FieldDefinition? field in clonedFields)
        {
            updateInfo.updatedFields.Add(field);
        }

        foreach (PropertyDefinition? property in clonedProperties)
        {
            updateInfo.updatedProperties.Add(property);
        }

        foreach (MethodDefinition? method in updatedMethods)
        {
            updateInfo.updatedMethods.Add(method);
        }

        // Add source and destination types to the update info
        updateInfo.srcTypes.Add(src);
        updateInfo.destTypes.Add(dest);
    }

    #endregion AddFieldsPropertiesAndMethods

    // Extension methods that handle the updating of fields, properties, and methods within a destination type after they have been cloned from a source type.
    // These methods ensure that the newly added components in the destination type correctly reference the destination type, rather than the original source type.

    #region UpdateFieldsPropertiesAndMethods

    /// <summary>
    /// Updates the types of attributes, interfaces, fields, properties, and methods within a given assembly.
    /// This includes updating the types in interfaces, fields, properties, and methods. It also updates the getter and setter methods for properties,
    /// updates the instruction types for methods, imports references for attributes, interfaces, fields, properties, and methods,
    /// imports base types of each destination type, and swaps any duplicate methods in the destination types.
    /// </summary>
    /// <param name="assembly">The assembly to be updated. This assembly's types are matched against the source types and replaced with the corresponding destination types, based on previously registered update information.</param>
    /// <param name="avoidSignatureConflicts">Avoid signature conflicts by changing original method parameters to be base object types for duplicate methods</param>
    public static void UpdateFieldsPropertiesAndMethods(this AssemblyDefinition assembly,
        bool avoidSignatureConflicts = false)
    {
        // Check if update information exists for the assembly
        if (assemblyUpdateInfo.TryGetValue(assembly, out UpdateInfo? updateInfo))
        {
            // Update types in interfaces, fields, properties, and methods
            for (int i = 0; i < updateInfo.destTypes.Count; ++i)
            {
                foreach (InterfaceImplementation? @interface in updateInfo.updatedInterfaces)
                {
                    @interface.UpdateTypes(updateInfo.srcTypes[i], updateInfo.destTypes[i]);
                }
            }

            for (int i = 0; i < updateInfo.destTypes.Count; ++i)
            {
                foreach (FieldDefinition? field in updateInfo.updatedFields)
                {
                    field.UpdateTypes(updateInfo.srcTypes[i], updateInfo.destTypes[i]);
                }
            }

            for (int i = 0; i < updateInfo.destTypes.Count; ++i)
            {
                foreach (PropertyDefinition? property in updateInfo.updatedProperties)
                {
                    property.UpdateTypes(updateInfo.srcTypes[i], updateInfo.destTypes[i]);
                }
            }

            for (int i = 0; i < updateInfo.destTypes.Count; ++i)
            {
                foreach (MethodDefinition? method in updateInfo.updatedMethods)
                {
                    method.UpdateTypes(updateInfo.srcTypes[i], updateInfo.destTypes[i]);
                }
            }

            // Update getter and setter methods for properties
            for (int i = 0; i < updateInfo.destTypes.Count; ++i)
            {
                foreach (PropertyDefinition? property in updateInfo.updatedProperties)
                {
                    property.UpdateGettersAndSetters(updateInfo.srcTypes[i], updateInfo.destTypes[i]);
                }
            }

            // Update instruction types for methods
            for (int i = 0; i < updateInfo.destTypes.Count; ++i)
            {
                foreach (MethodDefinition? method in updateInfo.updatedMethods)
                {
                    method.UpdateInstructionTypes(updateInfo.srcTypes[i], updateInfo.destTypes[i]);
                }
            }

            // Check for optimization opportunities for methods
            foreach (MethodDefinition? method in updateInfo.updatedMethods)
            {
                method.OptimizeInstructions();
            }

            // Import references for attributes, interfaces, fields, properties, and methods
            foreach (CustomAttribute? attribute in updateInfo.updatedAttributes)
            {
                attribute.ImportReferences(assembly.MainModule);
            }

            foreach (InterfaceImplementation? @interface in updateInfo.updatedInterfaces)
            {
                @interface.ImportReferences(assembly.MainModule);
            }

            foreach (FieldDefinition? field in updateInfo.updatedFields)
            {
                field.ImportReferences(assembly.MainModule);
            }

            foreach (PropertyDefinition? property in updateInfo.updatedProperties)
            {
                property.ImportReferences(assembly.MainModule);
            }

            foreach (MethodDefinition? method in updateInfo.updatedMethods)
            {
                method.ImportReferences(assembly.MainModule);
            }

            // Import base type of each dest type
            foreach (TypeDefinition? type in updateInfo.destTypes)
            {
                type.BaseType = assembly.MainModule.ImportReference(type.BaseType);
            }

            // Swap any duplicate methods in the destination types
            foreach (TypeDefinition? type in updateInfo.destTypes)
            {
                type.SwapDuplicateMethods(avoidSignatureConflicts);
            }

            // Remove the assembly from the update information collection
            _ = assemblyUpdateInfo.Remove(assembly);
        }
    }

    #endregion UpdateFieldsPropertiesAndMethods

    // Extension methods for cloning various Mono.Cecil objects.

    #region Clone

    /// <summary>
    /// Clones a CustomAttribute.
    /// </summary>
    /// <param name="attribute">The attribute to be cloned.</param>
    /// <returns>A clone of the original attribute.</returns>
    public static CustomAttribute Clone(this CustomAttribute attribute)
    {
        // Create a new CustomAttribute with the constructor of the original attribute.
        CustomAttribute? clonedAttribute = new(attribute.Constructor);

        // Add all constructor arguments from the original attribute to the cloned attribute.
        foreach (CustomAttributeArgument argument in attribute.ConstructorArguments)
        {
            clonedAttribute.ConstructorArguments.Add(argument);
        }

        // Add all properties from the original attribute to the cloned attribute.
        foreach (CustomAttributeNamedArgument property in attribute.Properties)
        {
            clonedAttribute.Properties.Add(property);
        }

        // Add all fields from the original attribute to the cloned attribute.
        foreach (CustomAttributeNamedArgument field in attribute.Fields)
        {
            clonedAttribute.Fields.Add(field);
        }

        // Return the cloned attribute.
        return clonedAttribute;
    }

    /// <summary>
    /// Clones a InterfaceImplementation.
    /// </summary>
    /// <param name="interface">The interface to be cloned.</param>
    /// <returns>A clone of the original interface.</returns>
    public static InterfaceImplementation Clone(this InterfaceImplementation @interface)
    {
        // Create a new InterfaceImplementation with the type the original interface.
        InterfaceImplementation? clonedInterface = new(@interface.InterfaceType);

        // Copy all custom attributes from the original interface to the cloned interface.
        foreach (CustomAttribute? attribute in @interface.CustomAttributes)
        {
            clonedInterface.CustomAttributes.Add(attribute.Clone());
        }

        // Return the cloned interface.
        return clonedInterface;
    }

    /// <summary>
    /// Clones a FieldDefinition.
    /// </summary>
    /// <param name="field">The field to be cloned.</param>
    /// <returns>A clone of the original field.</returns>
    public static FieldDefinition Clone(this FieldDefinition field)
    {
        // Create a new FieldDefinition with the same properties as the original field.
        FieldDefinition? clonedField = new(field.Name, field.Attributes, field.FieldType);

        // Copy all custom attributes from the original field to the cloned field.
        foreach (CustomAttribute? attribute in field.CustomAttributes)
        {
            clonedField.CustomAttributes.Add(attribute.Clone());
        }

        // Copy the MarshalInfo if it exists.
        clonedField.MarshalInfo = field.MarshalInfo != null ? new MarshalInfo(field.MarshalInfo.NativeType) : null;

        // Copy the initial value of the field.
        clonedField.InitialValue = field.InitialValue;

        // Return the cloned field.
        return clonedField;
    }

    /// <summary>
    /// Clones a PropertyDefinition.
    /// </summary>
    /// <param name="property">The property to be cloned.</param>
    /// <returns>A clone of the original property.</returns>
    public static PropertyDefinition Clone(this PropertyDefinition property)
    {
        // Create a new PropertyDefinition with the same properties as the original property.
        PropertyDefinition? clonedProperty = new(property.Name, property.Attributes, property.PropertyType);

        // Copy all custom attributes from the original property to the cloned property.
        foreach (CustomAttribute? attribute in property.CustomAttributes)
        {
            clonedProperty.CustomAttributes.Add(attribute.Clone());
        }

        // Clone the get and set methods if they exist.
        clonedProperty.GetMethod = property.GetMethod?.Clone();
        clonedProperty.SetMethod = property.SetMethod?.Clone();

        // Return the cloned property.
        return clonedProperty;
    }

    /// <summary>
    /// Clones a ParameterDefinition.
    /// </summary>
    /// <param name="parameter">The parameter to be cloned.</param>
    /// <returns>A clone of the original parameter.</returns>
    public static ParameterDefinition Clone(this ParameterDefinition parameter)
    {
        // Create a new ParameterDefinition with the same properties as the original parameter.
        ParameterDefinition? clonedParameter = new(parameter.Name, parameter.Attributes, parameter.ParameterType);

        // Copy all custom attributes from the original parameter to the cloned parameter.
        foreach (CustomAttribute? attribute in parameter.CustomAttributes)
        {
            clonedParameter.CustomAttributes.Add(attribute.Clone());
        }

        // Return the cloned parameter.
        return clonedParameter;
    }

    /// <summary>
    /// Clones a VariableDefinition.
    /// </summary>
    /// <param name="variable">The variable to be cloned.</param>
    /// <returns>A clone of the original variable.</returns>
    public static VariableDefinition Clone(this VariableDefinition variable) =>
        // Create and return a new VariableDefinition with the same type as the original variable.
        new(variable.VariableType);

    /// <summary>
    /// Clones an Instruction.
    /// </summary>
    /// <param name="instruction">The instruction to be cloned.</param>
    /// <returns>A clone of the original instruction.</returns>
    public static Instruction Clone(this Instruction instruction)
    {
        // Create a new Instruction with a default opcode.
        Instruction? clonedInstruction = Instruction.Create(OpCodes.Nop);

        // Copy the opcode and operand from the original instruction to the cloned instruction.
        clonedInstruction.OpCode = instruction.OpCode;
        clonedInstruction.Operand = instruction.Operand;

        // Return the cloned instruction.
        return clonedInstruction;
    }

    /// <summary>
    /// Clones all instructions in the collection.
    /// </summary>
    /// <param name="instructions">The collection of instructions to be cloned.</param>
    /// <returns>A new collection containing clones of the original instructions.</returns>
    public static Collection<Instruction> Clone(this Collection<Instruction> instructions)
    {
        if (instructions == null)
        {
            throw new ArgumentNullException(nameof(instructions));
        }

        Collection<Instruction>? clonedInstructions = new();

        foreach (Instruction? instruction in instructions)
        {
            // Add to the cloned collection
            clonedInstructions.Add(instruction.Clone());
        }

        return clonedInstructions;
    }

    /// <summary>
    /// Clones a MethodDefinition.
    /// </summary>
    /// <param name="method">The method to be cloned.</param>
    /// <returns>A clone of the original method.</returns>
    public static MethodDefinition Clone(this MethodDefinition method)
    {
        // Create a new MethodDefinition with the same properties as the original method.
        MethodDefinition? clonedMethod = new(method.Name, method.Attributes, method.ReturnType)
        {
            ImplAttributes = method.ImplAttributes, SemanticsAttributes = method.SemanticsAttributes
        };

        // Add all overides from the original method to the cloned method (references).
        foreach (MethodReference? @override in method.Overrides)
        {
            clonedMethod.Overrides.Add(@override);
        }

        // Copy all custom attributes from the original method to the cloned method.
        foreach (CustomAttribute? attribute in method.CustomAttributes)
        {
            clonedMethod.CustomAttributes.Add(attribute.Clone());
        }

        // Clone all parameters and add them to the cloned method.
        foreach (ParameterDefinition? parameter in method.Parameters)
        {
            clonedMethod.Parameters.Add(parameter.Clone());
        }

        // Create a new method body for the cloned method.
        clonedMethod.Body = new MethodBody(clonedMethod);

        // If the original method has a body, copy the relevant properties to the cloned method's body.
        if (method.HasBody)
        {
            clonedMethod.Body.MaxStackSize = method.Body.MaxStackSize;
            clonedMethod.Body.InitLocals = method.Body.InitLocals;

            // Clone all variables and add them to the cloned method's body.
            foreach (VariableDefinition? variable in method.Body.Variables)
            {
                clonedMethod.Body.Variables.Add(variable.Clone());
            }

            // Instruction mapping from old to new instructions used to update branch targets which is necessary after cloning
            Dictionary<Instruction, Instruction>? instructionMapping = new();

            // Clone all the instructions and create the mapping.
            foreach (Instruction? instruction in method.Body.Instructions)
            {
                Instruction? clonedInstruction = instruction.Clone();
                instructionMapping[instruction] = clonedInstruction;
                clonedMethod.Body.Instructions.Add(clonedInstruction);
            }

            // Now fix up the branch targets.
            foreach (Instruction? instruction in clonedMethod.Body.Instructions)
            {
                switch (instruction.OpCode.OperandType)
                {
                    // If the instruction is a branch instruction, fix up its target.
                    case OperandType.ShortInlineBrTarget:
                    case OperandType.InlineBrTarget:
                        instruction.Operand = instructionMapping[(Instruction)instruction.Operand];
                        break;
                    // If the instruction is a switch instruction, fix up its targets.
                    case OperandType.InlineSwitch:
                        Instruction[]? oldTargets = (Instruction[])instruction.Operand;
                        Instruction[]? newTargets = new Instruction[oldTargets.Length];
                        for (int i = 0; i < oldTargets.Length; ++i)
                        {
                            newTargets[i] = instructionMapping[oldTargets[i]];
                        }

                        instruction.Operand = newTargets;
                        break;
                }
            }
        }

        // Return the cloned method.
        return clonedMethod;
    }

    #endregion Clone

    // Extension methods for replacing references to a source type with references to a destination type within Mono.Cecil objects.
    // This is used to ensure that copied fields, properties, and methods reference copied types instead of the originals.

    #region UpdateTypes

    /// <summary>
    /// Updates the InterfaceType of the given InterfaceImplementation, if it matches the source type, to the destination type.
    /// </summary>
    /// <param name="interface">InterfaceImplementation that may have its InterfaceType updated.</param>
    /// <param name="src">The source type which could be replaced.</param>
    /// <param name="dest">The destination type which could replace the source type.</param>
    public static void UpdateTypes(this InterfaceImplementation @interface, TypeDefinition src, TypeDefinition dest)
    {
        // If the interface's type matches the source type, update it to the destination type
        if (@interface.InterfaceType == src)
        {
            @interface.InterfaceType = dest;
        }
    }

    /// <summary>
    /// Updates the FieldType of the given FieldDefinition, if it matches the source type, to the destination type.
    /// </summary>
    /// <param name="field">FieldDefinition that may have its FieldType updated.</param>
    /// <param name="src">The source type which could be replaced.</param>
    /// <param name="dest">The destination type which could replace the source type.</param>
    public static void UpdateTypes(this FieldDefinition field, TypeDefinition src, TypeDefinition dest)
    {
        // If the field's type matches the source type, update it to the destination type
        if (field.FieldType == src)
        {
            field.FieldType = dest;
        }
    }

    /// <summary>
    /// Updates the FieldReference and DeclaringType of the given FieldReference, if they match the source type, to the destination type.
    /// If a matching field definition is found in the destination type, a reference to it is returned.
    /// Otherwise, the original field reference is returned.
    /// </summary>
    /// <param name="field">FieldReference that may have its FieldType, and DeclaringType updated.</param>
    /// <param name="src">The source type which could be replaced.</param>
    /// <param name="dest">The destination type which could replace the source type.</param>
    /// <returns>A FieldReference with updated types, or the original FieldReference if no updates were made.</returns>
    public static FieldReference UpdateTypes(this FieldReference field, TypeDefinition src, TypeDefinition dest)
    {
        // Check if the field's FieldType or DeclaringType matches the source type, and if so, replace them with the destination type
        if (field.FieldType == src)
        {
            field.FieldType = dest;
        }

        if (field.DeclaringType == src)
        {
            field.DeclaringType = dest;
        }

        // Attempt to find a field in the destination type that matches the field's full name
        // If a matching definition is found, return a reference to it otherwise return original reference
        return dest.FindField(field.FullName) ?? field;
    }

    /// <summary>
    /// Updates the PropertyType of the given PropertyDefinition, if it matches the source type, to the destination type.
    /// </summary>
    /// <param name="property">PropertyDefinition that may have its PropertyType updated.</param>
    /// <param name="src">The source type which could be replaced.</param>
    /// <param name="dest">The destination type which could replace the source type.</param>
    public static void UpdateTypes(this PropertyDefinition property, TypeDefinition src, TypeDefinition dest)
    {
        // If the property's type matches the source type, update it to the destination type
        if (property.PropertyType == src)
        {
            property.PropertyType = dest;
        }
    }

    /// <summary>
    /// Updates the ParameterType of the given ParameterDefinition, if it matches the source type, to the destination type.
    /// </summary>
    /// <param name="parameter">ParameterDefinition that may have its ParameterType updated.</param>
    /// <param name="src">The source type which could be replaced.</param>
    /// <param name="dest">The destination type which could replace the source type.</param>
    public static void UpdateTypes(this ParameterDefinition parameter, TypeDefinition src, TypeDefinition dest)
    {
        // If the parameter's type matches the source type, update it to the destination type
        if (parameter.ParameterType == src)
        {
            parameter.ParameterType = dest;
        }
    }

    /// <summary>
    /// Updates the VariableType of the given VariableDefinition, if it matches the source type, to the destination type.
    /// </summary>
    /// <param name="variable">VariableDefinition that may have its VariableType updated.</param>
    /// <param name="src">The source type which could be replaced.</param>
    /// <param name="dest">The destination type which could replace the source type.</param>
    public static void UpdateTypes(this VariableDefinition variable, TypeDefinition src, TypeDefinition dest)
    {
        // If the variable's type matches the source type, update it to the destination type
        if (variable.VariableType == src)
        {
            variable.VariableType = dest;
        }
    }

    /// <summary>
    /// Updates the ReturnType of the given MethodDefinition, if it matches the source type, to the destination type.
    /// Also updates ParameterTypes and VariableTypes of the MethodDefinition using the same rule.
    /// </summary>
    /// <param name="method">MethodDefinition that may have its ReturnType, ParameterTypes, and VariableTypes updated.</param>
    /// <param name="src">The source type which could be replaced.</param>
    /// <param name="dest">The destination type which could replace the source type.</param>
    public static void UpdateTypes(this MethodDefinition method, TypeDefinition src, TypeDefinition dest)
    {
        // Update method overrides if they match the source type
        for (int i = 0; i < method.Overrides.Count; i++)
        {
            method.Overrides[i] = method.Overrides[i].UpdateTypes(src, dest);
        }

        // If the method's return type matches the source type, update it to the destination type
        if (method.ReturnType == src)
        {
            method.ReturnType = dest;
        }

        // Update method parameters and variables if they match the source type
        foreach (ParameterDefinition? parameter in method.Parameters)
        {
            parameter.UpdateTypes(src, dest);
        }

        if (method.HasBody)
        {
            foreach (VariableDefinition? variable in method.Body.Variables)
            {
                variable.UpdateTypes(src, dest);
            }
        }
    }

    /// <summary>
    /// Updates the ReturnType and DeclaringType of the given MethodReference, if they match the source type, to the destination type.
    /// Also updates the ParameterTypes of the MethodReference using the same rule.
    /// If a matching method definition is found in the destination type, a reference to it is returned.
    /// Otherwise, the original method reference is returned.
    /// </summary>
    /// <param name="method">MethodReference that may have its ReturnType, DeclaringType and ParameterTypes updated.</param>
    /// <param name="src">The source type which could be replaced.</param>
    /// <param name="dest">The destination type which could replace the source type.</param>
    /// <returns>A MethodReference with updated types, or the original MethodReference if no updates were made.</returns>
    public static MethodReference UpdateTypes(this MethodReference method, TypeDefinition src, TypeDefinition dest)
    {
        // Update method parameters to destination type
        foreach (ParameterDefinition? parameter in method.Parameters)
        {
            parameter.UpdateTypes(src, dest);
        }

        // Check if the method's ReturnType or DeclaringType matches the source type, and if so, replace them with the destination type
        if (method.ReturnType == src)
        {
            method.ReturnType = dest;
        }

        if (method.DeclaringType == src)
        {
            method.DeclaringType = dest;
        }

        // Attempt to find a method in the destination type that matches the method's full name
        // If a matching definition is found, return a reference to it otherwise return original reference
        return dest.FindMethod(method.FullName) ?? method;
    }

    /// <summary>
    /// Updates the ReturnType and Parameters of the CallSite to the destination type, if they match the source type, to the destination type.
    /// </summary>
    /// <param name="callSite">CallSite that needs its return type and parameters updated.</param>
    /// <param name="src">The original type which is being replaced.</param>
    /// <param name="dest">The new type which is replacing the original type.</param>
    public static void UpdateTypes(this CallSite callSite, TypeDefinition src, TypeDefinition dest)
    {
        // Update callsite parameters to destination type
        foreach (ParameterDefinition? parameter in callSite.Parameters)
        {
            parameter.UpdateTypes(src, dest);
        }

        // If the current return type is the source type, update it to destination type
        if (callSite.ReturnType == src)
        {
            callSite.ReturnType = dest;
        }
    }

    #endregion UpdateTypes

    // Extension methods for replacing references to a source type with references to a destination type within Mono.Cecil.Instruction objects.
    // This is crucial for ensuring that the instructions within methods correctly reference the fields, properties, and methods of the destination type after cloning from the source type.

    #region UpdateInstructionTypes

    /// <summary>
    /// Updates the Operand of an instruction when merging classes.
    /// The update strategy depends on the type of the operand.
    /// If the operand is a ParameterDefinition, VariableDefinition, FieldReference, MethodReference, CallSite, or TypeReference, it's updated accordingly.
    /// </summary>
    /// <param name="instruction">Instruction that needs its operand updated.</param>
    /// <param name="src">The original type which is being replaced.</param>
    /// <param name="dest">The new type which is replacing the original type.</param>
    public static void UpdateInstructionTypes(this Instruction instruction, TypeDefinition src, TypeDefinition dest)
    {
        // Check operand type and update accordingly
        if (instruction.Operand is ParameterDefinition parameter)
        {
            parameter.UpdateTypes(src, dest); // Update types in ParameterDefinition
        }
        else if (instruction.Operand is VariableDefinition variable)
        {
            variable.UpdateTypes(src, dest); // Update types in VariableDefinition
        }
        else if (instruction.Operand is TypeReference type && type == src)
        {
            instruction.Operand = dest; // Update type in TypeReference
        }
        else if (instruction.Operand is FieldReference field)
        {
            instruction.Operand = field.UpdateTypes(src, dest); // Update types in FieldReference
        }
        else if (instruction.Operand is MethodReference method)
        {
            instruction.Operand = method.UpdateTypes(src, dest); // Update types in MethodReference
        }
        else if (instruction.Operand is CallSite callSite)
        {
            callSite.UpdateTypes(src, dest); // Update types in CallSite
        }
    }

    /// <summary>
    /// Updates all instructions in the method's body.
    /// If the instruction's operand type matches the source type, it is replaced with the destination type.
    /// </summary>
    /// <param name="method">Method whose instructions are to be updated.</param>
    /// <param name="src">The original type which is being replaced.</param>
    /// <param name="dest">The new type which is replacing the original type.</param>
    public static void UpdateInstructionTypes(this MethodDefinition method, TypeDefinition src, TypeDefinition dest)
    {
        // Update instructions in the method body to the destination type
        if (method.HasBody)
        {
            foreach (Instruction? instruction in method.Body.Instructions)
            {
                UpdateInstructionTypes(instruction, src, dest);
            }
        }
    }

    #endregion UpdateInstructionTypes

    // Extension methods for replacing references to a source type with references to a destination type within Mono.Cecil.Property getter and setter methods.
    // This ensures that the properties of the destination type reference copied getters and setters instead of the originals.

    #region UpdateGettersAndSetters

    /// <summary>
    /// Updates the getter and setter methods of a property to reference the destination type when merging classes.
    /// This method does the following:
    ///     - Clones the existing getter/setter methods, so that any modifications do not affect the original methods
    ///     - Calls UpdateTypes to update all type references within the methods' bodies from src to dest
    ///     - Updates the declaring type of the methods to be dest
    ///     - Finds the equivalent methods in dest (if they exist), and updates the property's getter/setter methods to reference them
    /// This ensures that the property correctly interacts with the destination type after merging.
    /// </summary>
    /// <param name="property">PropertyDefinition whose getter and setter need to be updated.</param>
    /// <param name="src">The original type which is being replaced.</param>
    /// <param name="dest">The new type which is replacing the original type.</param>
    public static void UpdateGettersAndSetters(this PropertyDefinition property, TypeDefinition src,
        TypeDefinition dest)
    {
        // If the declaring type of the property is the destination type
        if (property.DeclaringType == dest)
        {
            // If the property has a getter, clone and update it
            if (property.GetMethod != null)
            {
                // Clone the getter
                MethodDefinition? clonedGetter = property.GetMethod.Clone();
                // Update all type references within the getter from src to dest
                clonedGetter.UpdateTypes(src, dest);
                // Update the declaring type of the getter to be dest
                clonedGetter.DeclaringType = dest;
                // If an equivalent method exists in dest, update the property's getter to reference it
                if (dest.FindMethod(clonedGetter.FullName) is MethodDefinition getMethod)
                {
                    property.GetMethod = getMethod;
                }
            }

            // If the property has a setter, clone and update it
            if (property.SetMethod != null)
            {
                // Clone the setter
                MethodDefinition? clonedSetter = property.SetMethod.Clone();
                // Update all type references within the setter from src to dest
                clonedSetter.UpdateTypes(src, dest);
                // Update the declaring type of the setter to be dest
                clonedSetter.DeclaringType = dest;
                // If an equivalent method exists in dest, update the property's setter to reference it
                if (dest.FindMethod(clonedSetter.FullName) is MethodDefinition setMethod)
                {
                    property.SetMethod = setMethod;
                }
            }
        }
    }

    #endregion UpdateGettersAndSetters

    // Extension methods to import references from one module to another.
    // This is important when merging assemblies classes as it allows the destination to access types that may not have been referenced prior.

    #region ImportReferences

    /// <summary>
    /// Imports the constructor reference for a given attribute into a module.
    /// </summary>
    /// <param name="attribute">The custom attribute whose constructor reference needs to be imported.</param>
    /// <param name="module">The module type into whose module the reference should be imported.</param>
    public static void ImportReferences(this CustomAttribute attribute, ModuleDefinition module) =>
        // Import the constructor reference into the module
        attribute.Constructor = module.ImportReference(attribute.Constructor);

    /// <summary>
    /// Imports the interface type and custom attributes references of an interface into a module.
    /// </summary>
    /// <param name="interface">The interface whose references need to be imported.</param>
    /// <param name="module">The module type into whose module the references should be imported.</param>
    public static void ImportReferences(this InterfaceImplementation @interface, ModuleDefinition module)
    {
        // Import the custom attributes references into the module
        foreach (CustomAttribute? attribute in @interface.CustomAttributes)
        {
            attribute.ImportReferences(module);
        }

        // Import the interface type reference into the module
        @interface.InterfaceType = module.ImportReference(@interface.InterfaceType);
    }

    /// <summary>
    /// Imports the field type and custom attributes references of a field into a module.
    /// </summary>
    /// <param name="field">The field whose references need to be imported.</param>
    /// <param name="module">The module type into whose module the references should be imported.</param>
    public static void ImportReferences(this FieldDefinition field, ModuleDefinition module)
    {
        // Import the custom attributes references into the module
        foreach (CustomAttribute? attribute in field.CustomAttributes)
        {
            attribute.ImportReferences(module);
        }

        // Import the field type reference into the module
        field.FieldType = module.ImportReference(field.FieldType);

        // Import the declaring type definition into the module
        field.DeclaringType = module.ImportReference(field.DeclaringType).Resolve();
    }

    /// <summary>
    /// Imports the property type and custom attributes references of a property into a module.
    /// </summary>
    /// <param name="property">The property whose references need to be imported.</param>
    /// <param name="module">The module type into whose module the references should be imported.</param>
    public static void ImportReferences(this PropertyDefinition property, ModuleDefinition module)
    {
        // Import the custom attributes references into the module
        foreach (CustomAttribute? attribute in property.CustomAttributes)
        {
            attribute.ImportReferences(module);
        }

        // Import the property type reference into the module
        property.PropertyType = module.ImportReference(property.PropertyType);

        // Import the declaring type definition into the module
        property.DeclaringType = module.ImportReference(property.DeclaringType).Resolve();
    }

    /// <summary>
    /// Imports the parameter type and custom attributes references of a parameter into a module.
    /// </summary>
    /// <param name="parameter">The parameter whose references need to be imported.</param>
    /// <param name="module">The module type into whose module the references should be imported.</param>
    public static void ImportReferences(this ParameterDefinition parameter, ModuleDefinition module)
    {
        // Import the custom attributes references into the module
        foreach (CustomAttribute? attribute in parameter.CustomAttributes)
        {
            attribute.ImportReferences(module);
        }

        // Import the parameter type reference into the module
        parameter.ParameterType = module.ImportReference(parameter.ParameterType);
    }

    /// <summary>
    /// Imports the variable type references of a variable into a module.
    /// </summary>
    /// <param name="variable">The variable whose type references need to be imported.</param>
    /// <param name="module">The module type into whose module the references should be imported.</param>
    public static void ImportReferences(this VariableDefinition variable, ModuleDefinition module) =>
        // Import the variable type reference into the module
        variable.VariableType = module.ImportReference(variable.VariableType);

    /// <summary>
    /// Imports the method type references and the custom attributes of a method into a module.
    /// </summary>
    /// <param name="method">The method whose references need to be imported.</param>
    /// <param name="module">The module type into whose module the references should be imported.</param>
    public static void ImportReferences(this MethodDefinition method, ModuleDefinition module)
    {
        // Import method overrides into the module
        for (int i = 0; i < method.Overrides.Count; ++i)
        {
            method.Overrides[i] = module.ImportReference(method.Overrides[i]);
        }

        // Import the custom attributes references into the module
        foreach (CustomAttribute? attribute in method.CustomAttributes)
        {
            attribute.ImportReferences(module);
        }

        // Import the parameter type references into the module
        foreach (ParameterDefinition? parameter in method.Parameters)
        {
            parameter.ImportReferences(module);
        }

        // Import the return type reference into the module
        method.ReturnType = module.ImportReference(method.ReturnType);

        // Import the declaring type definition into the module
        method.DeclaringType = module.ImportReference(method.DeclaringType).Resolve();

        // If the method has a body, import references for each variable and instruction
        if (method.HasBody)
        {
            // Import the variable type references in the method body into the module
            foreach (VariableDefinition? variable in method.Body.Variables)
            {
                variable.ImportReferences(module);
            }

            // Import the instruction type references in the method body into the module
            foreach (Instruction? instruction in method.Body.Instructions)
            {
                instruction.ImportReferences(module);
            }
        }
    }

    /// <summary>
    /// Imports the return type references of a CallSite into a module.
    /// </summary>
    /// <param name="callSite">The CallSite whose return type references need to be imported.</param>
    /// <param name="module">The module type into whose module the references should be imported.</param>
    public static void ImportReferences(this CallSite callSite, ModuleDefinition module) =>
        // Import the return type reference of the callSite into the module
        callSite.ReturnType = module.ImportReference(callSite.ReturnType);

    /// <summary>
    /// Imports the operand type references of an instruction into a module.
    /// </summary>
    /// <param name="instruction">The instruction whose operand references need to be imported.</param>
    /// <param name="module">The module type into whose module the references should be imported.</param>
    public static void ImportReferences(this Instruction instruction, ModuleDefinition module)
    {
        // Import the operand references of the instruction into the module
        if (instruction.Operand is ParameterDefinition parameter)
        {
            parameter.ImportReferences(module);
        }
        else if (instruction.Operand is VariableDefinition variable)
        {
            variable.ImportReferences(module);
        }
        else if (instruction.Operand is TypeReference type)
        {
            instruction.Operand = module.ImportReference(type);
        }
        else if (instruction.Operand is FieldReference field)
        {
            instruction.Operand = module.ImportReference(field);
        }
        else if (instruction.Operand is MethodReference method)
        {
            instruction.Operand = module.ImportReference(method);
        }
        else if (instruction.Operand is CallSite callSite)
        {
            callSite.ImportReferences(module);
        }
    }

    #endregion ImportReferences

    // Extension methods for swapping method implementations between different types.
    // This can be used when wanting to replace method functionality in the destination type with the corresponding functionality from the source type.

    #region SwapMethods

    /// <summary>
    /// Swaps the method references within the provided instruction between two given methods.
    /// </summary>
    /// <param name="instruction">The instruction to modify.</param>
    /// <param name="leftMethod">The first method to swap.</param>
    /// <param name="rightMethod">The second method to swap.</param>
    public static void SwapMethodReferences(this Instruction instruction, MethodDefinition leftMethod,
        MethodDefinition rightMethod)
    {
        // If the instruction's operand is a method reference
        if (instruction.Operand is MethodReference method)
        {
            // If the operand matches the left method, replace it with the right method
            if (method == leftMethod)
            {
                instruction.Operand = rightMethod;
            }
            // If the operand matches the right method, replace it with the left method
            else if (method == rightMethod)
            {
                instruction.Operand = leftMethod;
            }
        }
    }

    /// <summary>
    /// Swaps the method references within the provided collection of instructions between two given methods.
    /// </summary>
    /// <param name="instructions">The collection of instructions to modify.</param>
    /// <param name="leftMethod">The first method to swap.</param>
    /// <param name="rightMethod">The second method to swap.</param>
    public static void SwapMethodReferences(this Collection<Instruction> instructions, MethodDefinition leftMethod,
        MethodDefinition rightMethod)
    {
        // Swap method references for each instruction in the collection
        foreach (Instruction? instruction in instructions)
        {
            instruction.SwapMethodReferences(leftMethod, rightMethod);
        }
    }

    /// <summary>
    /// Swaps the method references within the body of the provided method between two given methods.
    /// </summary>
    /// <param name="method">The method to modify.</param>
    /// <param name="leftMethod">The first method to swap.</param>
    /// <param name="rightMethod">The second method to swap.</param>
    public static void SwapMethodReferences(this MethodDefinition method, MethodDefinition leftMethod,
        MethodDefinition rightMethod)
    {
        // Swap method references for each instruction in the method's body
        if (method.HasBody)
        {
            method.Body.Instructions.SwapMethodReferences(leftMethod, rightMethod);
        }
    }

    /// <summary>
    /// Swaps the attributes, parameters, custom attributes, and generic parameters between two given methods.
    /// </summary>
    /// <param name="leftMethod">The first method to swap.</param>
    /// <param name="rightMethod">The second method to swap.</param>
    public static void SwapMethods(this MethodDefinition leftMethod, MethodDefinition rightMethod)
    {
        // Save the left method's original details
        MethodBody? leftBody = leftMethod.Body;
        MethodAttributes leftAttributes = leftMethod.Attributes;
        MethodImplAttributes leftImplAttributes = leftMethod.ImplAttributes;
        MethodSemanticsAttributes leftSemanticsAttributes = leftMethod.SemanticsAttributes;
        Collection<ParameterDefinition>? leftParameters = new(leftMethod.Parameters);
        Collection<CustomAttribute>? leftCustomAttributes = new(leftMethod.CustomAttributes);
        Collection<GenericParameter>? leftGenericParameters = new(leftMethod.GenericParameters);

        // Swap the details from the right method to the left
        leftMethod.Body = rightMethod.Body;
        leftMethod.Body = rightMethod.Body;
        leftMethod.Attributes = rightMethod.Attributes;
        leftMethod.ImplAttributes = rightMethod.ImplAttributes;
        leftMethod.SemanticsAttributes = rightMethod.SemanticsAttributes;
        leftMethod.Parameters.Clear();
        leftMethod.CustomAttributes.Clear();
        leftMethod.GenericParameters.Clear();
        foreach (ParameterDefinition? parameter in rightMethod.Parameters)
        {
            leftMethod.Parameters.Add(parameter);
        }

        foreach (CustomAttribute? attribute in rightMethod.CustomAttributes)
        {
            leftMethod.CustomAttributes.Add(attribute);
        }

        foreach (GenericParameter? parameter in rightMethod.GenericParameters)
        {
            leftMethod.GenericParameters.Add(parameter);
        }

        // Swap the details from the left method (which were saved) to the right
        rightMethod.Body = leftBody;
        rightMethod.Body = leftBody;
        rightMethod.Attributes = leftAttributes;
        rightMethod.ImplAttributes = leftImplAttributes;
        rightMethod.SemanticsAttributes = leftSemanticsAttributes;
        rightMethod.Parameters.Clear();
        rightMethod.CustomAttributes.Clear();
        rightMethod.GenericParameters.Clear();
        foreach (ParameterDefinition? parameter in leftParameters)
        {
            rightMethod.Parameters.Add(parameter);
        }

        foreach (CustomAttribute? attribute in leftCustomAttributes)
        {
            rightMethod.CustomAttributes.Add(attribute);
        }

        foreach (GenericParameter? parameter in leftGenericParameters)
        {
            rightMethod.GenericParameters.Add(parameter);
        }

        // Swap method references within each method body
        leftMethod.SwapMethodReferences(leftMethod, rightMethod);
        rightMethod.SwapMethodReferences(rightMethod, leftMethod);
    }

    /// <summary>
    /// Finds and swaps methods with the same full name within the given type.
    /// </summary>
    /// <param name="type">The type to modify.</param>
    /// <param name="avoidSignatureConflicts">Avoid signature conflicts by changing original method parameters to be base object types</param>
    public static void SwapDuplicateMethods(this TypeDefinition type, bool avoidSignatureConflicts = false)
    {
        // This HashSet is used for tracking the methods that have already been swapped.
        HashSet<string>? alreadySwapped = new();

        // Convert the method collection to list for efficient index-based access.
        List<MethodDefinition>? methods = type.Methods.ToList();

        // Iterate over each pair of methods in the type
        for (int i = 0; i < methods.Count; i++)
        {
            for (int j = i + 1; j < methods.Count; j++)
            {
                MethodDefinition? methodLeft = methods[i];
                MethodDefinition? methodRight = methods[j];

                // If two methods have the same full name and haven't been swapped yet
                if (methodLeft.FullName == methodRight.FullName && !alreadySwapped.Contains(methodLeft.FullName))
                {
                    // Add the method full name to the set of already swapped methods
                    _ = alreadySwapped.Add(methodLeft.FullName);
                    // Swap the two methods
                    methodLeft.SwapMethods(methodRight);

                    // Change the original method types to be generic to avoid signature conflicts
                    if (avoidSignatureConflicts)
                    {
                        foreach (ParameterDefinition? parameter in methodRight.Parameters)
                        {
                            if (!parameter.ParameterType.IsValueType)
                            {
                                parameter.ParameterType = type.Module.ImportReference(typeof(object));
                            }
                        }
                    }
                }
            }
        }
    }

    #endregion SwapMethods

    // Methods to do with instruction optimizations

    #region InstructionOptimizations

#pragma warning disable RCS1003
    /// <summary>
    /// Determines if a given instruction within a method can be optimized out.
    /// Specifically, this method looks for type conversion instructions (Isinst or Castclass)
    /// that are unnecessary because the type of the value at the top of the stack is
    /// already the target conversion type.
    /// </summary>
    /// <param name="instruction">The instruction to be checked for optimization.</param>
    /// <param name="method">The method definition that contains the instruction.</param>
    /// <returns>Returns true if the instruction can be optimized out. Otherwise, returns false.</returns>
    /// <remarks>
    /// This method works by examining the instructions before the given instruction in the method,
    /// maintaining a conceptual "stack balance" and tracking the type of the value at the top of the stack.
    /// The stack balance is a measure of the net effect of the instructions on the stack,
    /// with a positive balance indicating more values have been pushed than popped,
    /// and a negative balance indicating more values have been popped than pushed.
    /// If the stack balance is zero and the type of the value at the top of the stack
    /// matches the type conversion, the conversion is unnecessary and the method returns true.
    /// </remarks>
    public static bool CanBeOptimizedOut(this Instruction instruction, MethodDefinition method)
    {
        // Check if the instruction is a type conversion instruction (instance cast or class cast)
        if (instruction.OpCode == OpCodes.Isinst || instruction.OpCode == OpCodes.Castclass)
        {
            // Get the type to which the conversion is being made
            TypeReference? typeConversionType = instruction.Operand as TypeReference;
            // Initialize stack balance. This will help to determine the net stack effect of the instructions
            int stackBalance = 0;
            // Move to the previous instruction
            instruction = instruction.Previous;

            // Process previous instructions
            while (instruction != null)
            {
                // Determine how the current instruction modifies the stack
                StackBehaviour pushBehaviour = instruction.OpCode.StackBehaviourPush;
                StackBehaviour popBehaviour = instruction.OpCode.StackBehaviourPop;

                // Fullname of any type extracted from the instruction
                string extractedFullName = null;

                // This is an exhaustive check for control flow change instructions. These instructions will cause a jump
                // in execution or a termination of the function, thus ending our analysis.
                if (instruction.OpCode == OpCodes.Ret || // Return from the current method.
                    instruction.OpCode == OpCodes.Throw || // Throw an exception.
                    instruction.OpCode == OpCodes.Rethrow || // Rethrow the current exception.
                    instruction.OpCode ==
                    OpCodes.Endfilter || // End the filter clause of an exception block and branch to the exception handler.
                    instruction.OpCode ==
                    OpCodes.Endfinally || // Transfer control from the exception block of a try or catch block.
                    instruction.OpCode == OpCodes.Leave ||
                    instruction.OpCode == OpCodes.Leave_S || // Exit a protected region of code.
                    instruction.OpCode ==
                    OpCodes.Jmp || // Jump to the method pointed to by the method pointer loaded on the stack.
                    instruction.OpCode == OpCodes.Switch || // Switch control to one of several locations.
                    instruction.OpCode == OpCodes.Br ||
                    instruction.OpCode == OpCodes.Br_S || // Unconditional branch to target.
                    instruction.OpCode == OpCodes.Brfalse ||
                    instruction.OpCode == OpCodes.Brfalse_S || // Branch to target if value is zero (false).
                    instruction.OpCode == OpCodes.Brtrue ||
                    instruction.OpCode == OpCodes.Brtrue_S || // Branch to target if value is non-zero (true).
                    instruction.OpCode == OpCodes.Beq ||
                    instruction.OpCode == OpCodes.Beq_S || // Branch to target if two values are equal.
                    instruction.OpCode == OpCodes.Bne_Un ||
                    instruction.OpCode == OpCodes.Bne_Un_S || // Branch to target if two values are not equal.
                    instruction.OpCode == OpCodes.Bge || instruction.OpCode == OpCodes.Bge_S ||
                    instruction.OpCode == OpCodes.Bge_Un ||
                    instruction.OpCode ==
                    OpCodes.Bge_Un_S || // Branch to target if value1 >= value2 (unsigned or unordered).
                    instruction.OpCode == OpCodes.Bgt || instruction.OpCode == OpCodes.Bgt_S ||
                    instruction.OpCode == OpCodes.Bgt_Un ||
                    instruction.OpCode ==
                    OpCodes.Bgt_Un_S || // Branch to target if value1 > value2 (unsigned or unordered).
                    instruction.OpCode == OpCodes.Ble || instruction.OpCode == OpCodes.Ble_S ||
                    instruction.OpCode == OpCodes.Ble_Un ||
                    instruction.OpCode ==
                    OpCodes.Ble_Un_S || // Branch to target if value1 <= value2 (unsigned or unordered).
                    instruction.OpCode == OpCodes.Blt || instruction.OpCode == OpCodes.Blt_S ||
                    instruction.OpCode == OpCodes.Blt_Un ||
                    instruction.OpCode ==
                    OpCodes.Blt_Un_S) // Branch to target if value1 < value2 (unsigned or unordered).
                {
                    return false; // Return from method
                }

                // Check if instruction is for loading a field onto the stack
                // In this case, the type of the value is the type of the field.
                else if (instruction.OpCode == OpCodes.Ldfld || // load field value onto stack
                         instruction.OpCode == OpCodes.Ldflda || // load field address onto stack
                         instruction.OpCode == OpCodes.Ldsfld || // load static field value onto stack
                         instruction.OpCode == OpCodes.Ldsflda) // load static field address onto stack
                {
                    extractedFullName = ((FieldReference)instruction.Operand).FieldType.FullName;
                }

                // Check if instruction is for loading an argument onto the stack
                // In this case, the type of the value is the type of the argument.
                else if (instruction.OpCode == OpCodes.Ldarg || // load argument onto stack
                         instruction.OpCode == OpCodes.Ldarg_S) // short form for loading argument onto stack
                {
                    extractedFullName = ((ParameterReference)instruction.Operand).ParameterType.FullName;
                }

                // Check for loading argument at index 0 onto stack
                else if (instruction.OpCode == OpCodes.Ldarg_0) // load argument at index 0 onto stack
                {
                    extractedFullName = (method.IsStatic ? method.Parameters[0].ParameterType : method.DeclaringType)
                        .FullName;
                }
                // Check for loading argument at index 1 onto stack
                else if (instruction.OpCode == OpCodes.Ldarg_1) // load argument at index 1 onto stack
                {
                    extractedFullName =
                        (method.IsStatic ? method.Parameters[1].ParameterType : method.Parameters[0].ParameterType)
                        .FullName;
                }
                // Check for loading argument at index 2 onto stack
                else if (instruction.OpCode == OpCodes.Ldarg_2) // load argument at index 2 onto stack
                {
                    extractedFullName =
                        (method.IsStatic ? method.Parameters[2].ParameterType : method.Parameters[1].ParameterType)
                        .FullName;
                }
                // Check for loading argument at index 3 onto stack
                else if (instruction.OpCode == OpCodes.Ldarg_3) // load argument at index 3 onto stack
                {
                    extractedFullName =
                        (method.IsStatic ? method.Parameters[3].ParameterType : method.Parameters[2].ParameterType)
                        .FullName;
                }

                // Check for loading local variable onto stack
                else if (instruction.OpCode == OpCodes.Ldloc || // load local variable onto stack
                         instruction.OpCode == OpCodes.Ldloc_S) // short form for loading local variable onto stack
                {
                    extractedFullName = ((VariableReference)instruction.Operand).VariableType.FullName;
                }
                // Check for loading local variable at index 0 onto stack
                else if (instruction.OpCode == OpCodes.Ldloc_0) // load local variable at index 0 onto stack
                {
                    extractedFullName = method.Body.Variables[0].VariableType.FullName;
                }
                // Check for loading local variable at index 1 onto stack
                else if (instruction.OpCode == OpCodes.Ldloc_1) // load local variable at index 1 onto stack
                {
                    extractedFullName = method.Body.Variables[1].VariableType.FullName;
                }
                // Check for loading local variable at index 2 onto stack
                else if (instruction.OpCode == OpCodes.Ldloc_2) // load local variable at index 2 onto stack
                {
                    extractedFullName = method.Body.Variables[2].VariableType.FullName;
                }
                // Check for loading local variable at index 3 onto stack
                else if (instruction.OpCode == OpCodes.Ldloc_3) // load local variable at index 3 onto stack
                {
                    extractedFullName = method.Body.Variables[3].VariableType.FullName;
                }

                // Check for calling a method and pushing return value onto the stack, loading function pointer onto the stack
                else if
                    (instruction.OpCode == OpCodes.Callvirt || // call method virtually and push return value onto stack
                     instruction.OpCode == OpCodes.Call || // call method and push return value onto stack
                     instruction.OpCode == OpCodes.Ldftn || // load method pointer onto stack
                     instruction.OpCode == OpCodes.Ldvirtftn) // load virtual method pointer onto stack
                {
                    extractedFullName = ((MethodReference)instruction.Operand).ReturnType.FullName;
                }

                // Check for calling a method indicated on the stack with arguments, pushing return value onto stack
                else if
                    (instruction.OpCode ==
                     OpCodes.Calli) // call method indicated on the stack with arguments, pushing return value onto stack
                {
                    extractedFullName = ((CallSite)instruction.Operand).ReturnType.FullName;
                }

                // Check for creating a new object and pushing object reference onto stack
                else if
                    (instruction.OpCode == OpCodes.Newobj) // create a new object and push object reference onto stack
                {
                    extractedFullName = ((MethodReference)instruction.Operand).DeclaringType.FullName;
                }

                // Check for loading an object, array element, or pointer onto stack, creating a new array, or creating a typed reference
                else if (instruction.OpCode == OpCodes.Ldobj || // load object onto stack
                         instruction.OpCode == OpCodes.Ldelem_Any || // load element of an object array onto stack
                         instruction.OpCode == OpCodes.Newarr || // create a new array and push reference onto stack
                         instruction.OpCode == OpCodes.Mkrefany) // push a typed reference onto stack
                {
                    extractedFullName = ((TypeReference)instruction.Operand).FullName;
                }

                // Check for loading a string onto stack
                else if (instruction.OpCode == OpCodes.Ldstr) // load a string onto stack
                {
                    extractedFullName = typeof(string).FullName;
                }

                // If the type of the value currently at the top of the stack matches the type conversion
                // and the stack is balanced, the conversion is unnecessary
                if (stackBalance == 0 && extractedFullName == typeConversionType.FullName)
                {
                    return true;
                }

                // Dup doesn't change the type of the top of the stack, so adjust stack balance to ignore it
                if (instruction.OpCode == OpCodes.Dup)
                {
                    stackBalance--;
                }

                // Adjust stack balance according to the current instruction's push behavior
                //if (pushBehaviour == StackBehaviour.Push0)
                if (pushBehaviour == StackBehaviour.Push1 || pushBehaviour == StackBehaviour.Pushi ||
                    pushBehaviour == StackBehaviour.Pushref ||
                    pushBehaviour == StackBehaviour.Pushi8 || pushBehaviour == StackBehaviour.Pushr4 ||
                    pushBehaviour == StackBehaviour.Pushr8 ||
                    pushBehaviour == StackBehaviour.Varpush)
                {
                    stackBalance++;
                }
                else if (pushBehaviour == StackBehaviour.Push1_push1)
                {
                    stackBalance += 2;
                }

                // Adjust stack balance according to the current instruction's pop behavior
                //if (popBehaviour == StackBehaviour.Pop0)
                if (popBehaviour == StackBehaviour.Pop1 || popBehaviour == StackBehaviour.Popi ||
                    popBehaviour == StackBehaviour.Popref ||
                    popBehaviour == StackBehaviour.Varpop)
                {
                    stackBalance--;
                }
                else if (popBehaviour == StackBehaviour.Pop1_pop1 || popBehaviour == StackBehaviour.Popi_popi ||
                         popBehaviour == StackBehaviour.Popi_pop1 ||
                         popBehaviour == StackBehaviour.Popi_popi8 || popBehaviour == StackBehaviour.Popi_popr4 ||
                         popBehaviour == StackBehaviour.Popi_popr8 ||
                         popBehaviour == StackBehaviour.Popref_pop1 || popBehaviour == StackBehaviour.Popref_popi)
                {
                    stackBalance -= 2;
                }
                else if (popBehaviour == StackBehaviour.Popi_popi_popi ||
                         popBehaviour == StackBehaviour.Popref_popi_popi ||
                         popBehaviour == StackBehaviour.Popref_popi_popi8 ||
                         popBehaviour == StackBehaviour.Popref_popi_popr4 ||
                         popBehaviour == StackBehaviour.Popref_popi_popr8 ||
                         popBehaviour == StackBehaviour.Popref_popi_popref)
                {
                    stackBalance -= 3;
                }

                // Move to previous instruction
                instruction = instruction.Previous;
            }
        }

        // Return that the instruction cannot be optimized
        return false;
    }
#pragma warning restore RCS1003

    /// <summary>
    /// Optimizes a given method by removing any instructions
    /// that can be optimized out.
    /// </summary>
    /// <param name="method">
    /// The MethodDefinition object to be optimized. This method contains a list
    /// of instructions that are to be checked and potentially removed if they can be optimized out.
    /// </param>
    public static void OptimizeInstructions(this MethodDefinition method)
    {
        // If the method doesn't have a body (i.e., it's an abstract or external method), then exit
        if (!method.HasBody)
        {
            return;
        }

        // Iterate over each instruction in the method body
        for (int i = 0; i < method.Body.Instructions.Count - 1; ++i)
        {
            // If the current instruction can be optimized out according to the CanBeOptimizedOut method, remove it
            if (method.Body.Instructions[i].CanBeOptimizedOut(method))
            {
                // Remove the current instruction from the method body
                method.Body.Instructions.RemoveAt(i);

                // Decrement the loop index to account for the removal. This ensures that the next iteration doesn't skip any instructions.
                i--;
            }
        }
    }

    #endregion InstructionOptimizations
}
#endif
