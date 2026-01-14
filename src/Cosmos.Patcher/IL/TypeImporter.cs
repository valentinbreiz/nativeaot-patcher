using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;

namespace Cosmos.Patcher.IL;

/// <summary>
/// Handles safe importing of type, method, and field references.
/// Fixes self-references where TypeRef incorrectly points to the same assembly instead of using TypeDef.
/// </summary>
public static class TypeImporter
{
    /// <summary>
    /// Imports a type reference, fixing self-references to use TypeDef instead of TypeRef.
    /// Prevents "Invalid TypeRef token" errors when a patched assembly references itself.
    /// </summary>
    public static TypeReference SafeImportType(ModuleDefinition module, TypeReference typeRef)
    {
        if (typeRef == null) return null!;

        var imported = module.ImportReference(typeRef);

        if (!MightHaveSelfReference(module, imported))
            return imported;

        return FixSelfReferences(module, imported);
    }

    /// <summary>
    /// Imports a method reference, fixing self-references in declaring type and parameters.
    /// </summary>
    public static MethodReference SafeImportMethod(ModuleDefinition module, MethodReference methodRef)
    {
        if (methodRef == null) return null!;

        var imported = module.ImportReference(methodRef);

        bool needsNewRef = false;
        var fixedDeclaringType = imported.DeclaringType;
        var fixedReturnType = imported.ReturnType;
        var fixedParams = new List<(string Name, ParameterAttributes Attrs, TypeReference Type)>();

        if (imported.DeclaringType != null && MightHaveSelfReference(module, imported.DeclaringType))
        {
            var fixed_ = FixSelfReferences(module, imported.DeclaringType);
            if (fixed_ != imported.DeclaringType)
            {
                fixedDeclaringType = fixed_;
                needsNewRef = true;
            }
        }

        if (imported.ReturnType != null && MightHaveSelfReference(module, imported.ReturnType))
        {
            var fixed_ = FixSelfReferences(module, imported.ReturnType);
            if (fixed_ != imported.ReturnType)
            {
                fixedReturnType = fixed_;
                needsNewRef = true;
            }
        }

        foreach (var param in imported.Parameters)
        {
            if (param.ParameterType != null && MightHaveSelfReference(module, param.ParameterType))
            {
                var fixed_ = FixSelfReferences(module, param.ParameterType);
                fixedParams.Add((param.Name, param.Attributes, fixed_));
                if (fixed_ != param.ParameterType)
                    needsNewRef = true;
            }
            else
            {
                fixedParams.Add((param.Name, param.Attributes, param.ParameterType!));
            }
        }

        if (needsNewRef)
        {
            if (fixedDeclaringType is TypeDefinition typeDef)
            {
                var methodDef = typeDef.Methods.FirstOrDefault(m =>
                    m.Name == imported.Name &&
                    m.Parameters.Count == fixedParams.Count &&
                    m.Parameters.Select(p => p.ParameterType.FullName)
                        .SequenceEqual(fixedParams.Select(p => p.Type.FullName)));

                if (methodDef != null)
                    return methodDef;
            }

            var newMethodRef = new MethodReference(imported.Name, fixedReturnType, fixedDeclaringType)
            {
                HasThis = imported.HasThis,
                ExplicitThis = imported.ExplicitThis,
                CallingConvention = imported.CallingConvention
            };

            foreach (var (name, attrs, type) in fixedParams)
                newMethodRef.Parameters.Add(new ParameterDefinition(name, attrs, type));

            foreach (var gp in imported.GenericParameters)
                newMethodRef.GenericParameters.Add(new GenericParameter(gp.Name, newMethodRef));

            return newMethodRef;
        }

        return imported;
    }

    /// <summary>
    /// Imports a field reference, fixing self-references in declaring type and field type.
    /// </summary>
    public static FieldReference SafeImportField(ModuleDefinition module, FieldReference fieldRef)
    {
        if (fieldRef == null) return null!;

        var imported = module.ImportReference(fieldRef);

        bool needsNewRef = false;
        var fixedDeclaringType = imported.DeclaringType;
        var fixedFieldType = imported.FieldType;

        if (imported.DeclaringType != null && MightHaveSelfReference(module, imported.DeclaringType))
        {
            var fixed_ = FixSelfReferences(module, imported.DeclaringType);
            if (fixed_ != imported.DeclaringType)
            {
                fixedDeclaringType = fixed_;
                needsNewRef = true;
            }
        }

        if (imported.FieldType != null && MightHaveSelfReference(module, imported.FieldType))
        {
            var fixed_ = FixSelfReferences(module, imported.FieldType);
            if (fixed_ != imported.FieldType)
            {
                fixedFieldType = fixed_;
                needsNewRef = true;
            }
        }

        if (needsNewRef)
            return new FieldReference(imported.Name, fixedFieldType, fixedDeclaringType);

        return imported;
    }

    /// <summary>
    /// Checks if a type might have self-references that need fixing.
    /// </summary>
    private static bool MightHaveSelfReference(ModuleDefinition module, TypeReference typeRef)
    {
        if (typeRef == null) return false;

        string targetAsmName = module.Assembly.Name.Name;

        if (typeRef.Scope is AssemblyNameReference asmRef && asmRef.Name == targetAsmName)
            return true;

        if (typeRef is GenericInstanceType git)
        {
            foreach (var arg in git.GenericArguments)
                if (MightHaveSelfReference(module, arg))
                    return true;
        }

        if (typeRef is TypeSpecification typeSpec)
            return MightHaveSelfReference(module, typeSpec.ElementType);

        return false;
    }

    /// <summary>
    /// Recursively fixes self-references in a type reference.
    /// When a type reference points to the same assembly as the module, replace it with the TypeDef.
    /// </summary>
    private static TypeReference FixSelfReferences(ModuleDefinition module, TypeReference typeRef)
    {
        if (typeRef == null) return null!;

        if (typeRef is GenericInstanceType git)
        {
            bool needsFix = false;
            var fixedArgs = new List<TypeReference>();

            foreach (var arg in git.GenericArguments)
            {
                var fixedArg = FixSelfReferences(module, arg);
                fixedArgs.Add(fixedArg);
                if (fixedArg != arg)
                    needsFix = true;
            }

            if (needsFix)
            {
                var result = new GenericInstanceType(git.ElementType);
                foreach (var arg in fixedArgs)
                    result.GenericArguments.Add(arg);
                return result;
            }
            return git;
        }

        if (typeRef is ArrayType arrayType)
        {
            var fixedElement = FixSelfReferences(module, arrayType.ElementType);
            if (fixedElement != arrayType.ElementType)
                return new ArrayType(fixedElement, arrayType.Rank);
            return arrayType;
        }

        if (typeRef is ByReferenceType byRefType)
        {
            var fixedElement = FixSelfReferences(module, byRefType.ElementType);
            if (fixedElement != byRefType.ElementType)
                return new ByReferenceType(fixedElement);
            return byRefType;
        }

        if (typeRef is PointerType ptrType)
        {
            var fixedElement = FixSelfReferences(module, ptrType.ElementType);
            if (fixedElement != ptrType.ElementType)
                return new PointerType(fixedElement);
            return ptrType;
        }

        if (typeRef is GenericParameter)
            return typeRef;

        if (typeRef.Scope is AssemblyNameReference asmRef && asmRef.Name == module.Assembly.Name.Name)
        {
            var typeDef = module.GetType(typeRef.FullName);
            if (typeDef != null)
                return typeDef;
        }

        return typeRef;
    }
}
