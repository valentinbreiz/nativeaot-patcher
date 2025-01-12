using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Liquip.API;

public static class LabelMaker
{
    /// <summary>
    /// Cache for label names.
    /// </summary>
    private static Dictionary<MethodBase, string> LabelNamesCache = new();

    private static Dictionary<Assembly, int> AssemblyIds = new();

    // All label naming code should be changed to use this class.

    // Label bases can be up to 200 chars. If larger they will be shortened with an included hash.
    // This leaves up to 56 chars for suffix information.

    // Suffixes are a series of tags and have their own prefixes to preserve backwards compat.
    // .GUID_xxxxxx
    // .IL_0000
    // .ASM_00 - future, currently is IL_0000 or IL_0000.00
    // Would be nice to combine IL and ASM into IL_0000_00, but the way we work with the assembler currently
    // we cant because the ASM labels are issued as local labels.
    //
    // - Methods use a variety of alphanumeric suffixes for support code.
    // - .00 - asm markers at beginning of method
    // - .0000.00 IL.ASM marker

    public static int LabelCount { get; private set; }

    // Max length of labels at 256. We use lower here so that we still have room for suffixes for IL positions, etc.
    private const int MaxLengthWithoutSuffix = 200;

    public static string Get(MethodBase aMethod)
    {
        if (LabelNamesCache.TryGetValue(aMethod, out string? result))
        {
            return result;
        }

        result = Final(GetFullName(aMethod));
        LabelNamesCache.Add(aMethod, result);
        return result;
    }

    private const string IllegalIdentifierChars = "&.,+$<>{}-`\'/\\ ()[]*!=";

    // no array bracket, they need to replace, for unique names for used types in methods
    private static readonly Regex IllegalCharsReplace = new(@"[&.,+$<>{}\-\`\\'/\\ \(\)\*!=]", RegexOptions.Compiled);

    private static string FilterStringForIncorrectChars(string aName)
    {
        string? xTempResult = aName;
        foreach (char c in IllegalIdentifierChars)
        {
            xTempResult = xTempResult.Replace(c, '_');
        }

        return xTempResult;
    }

    private static string Final(string xName)
    {
        xName = xName.Replace("[]", "array");
        xName = xName.Replace("<>", "compilergenerated");
        xName = xName.Replace("[,]", "array");
        xName = xName.Replace("*", "pointer");
        xName = xName.Replace("|", "sLine");

        xName = IllegalCharsReplace.Replace(xName, string.Empty);

        if (xName.Length > MaxLengthWithoutSuffix)
        {
            using (MD5? xHash = MD5.Create())
            {
                byte[]? xValue = xHash.ComputeHash(Encoding.GetEncoding(0).GetBytes(xName));
                StringBuilder? xSb = new(xName);
                // Keep length max same as before.
                xSb.Length = MaxLengthWithoutSuffix - xValue.Length * 2;
                foreach (byte xByte in xValue)
                {
                    xSb.Append(xByte.ToString("X2"));
                }

                xName = xSb.ToString();
            }
        }

        LabelCount++;
        return xName;
    }

    /// <summary>
    /// Get internal name for the type
    /// </summary>
    /// <param name="aType"></param>
    /// <param name="aAssemblyIncluded">If true, the assembly id is included</param>
    /// <returns></returns>
    public static string GetFullName(Type? aType, bool aAssemblyIncluded = true)
    {
        if (aType is null)
        {
            throw new ArgumentException("type is null", nameof(aType));
        }

        if (aType.IsGenericParameter)
        {
            return aType.FullName;
        }

        StringBuilder? stringBuilder = new(256);

        if (aAssemblyIncluded)
        {
            // Start the string with the id of the assembly
            Assembly? assembly = aType.Assembly;
            if (!AssemblyIds.ContainsKey(assembly))
            {
                AssemblyIds.Add(assembly, AssemblyIds.Count);
            }

            stringBuilder.Append("A" + AssemblyIds[assembly]);
        }

        if (aType.IsArray)
        {
            stringBuilder.Append(GetFullName(aType.GetElementType(), aAssemblyIncluded));
            stringBuilder.Append("[");
            int xRank = aType.GetArrayRank();
            while (xRank > 1)
            {
                stringBuilder.Append(",");
                xRank--;
            }

            stringBuilder.Append("]");
            return stringBuilder.ToString();
        }

        if (aType is { IsByRef: true, HasElementType: true })
        {
            return "&" + GetFullName(aType.GetElementType(), aAssemblyIncluded);
        }

        if (aType is { IsGenericType: true, IsGenericTypeDefinition: false })
        {
            stringBuilder.Append(GetFullName(aType.GetGenericTypeDefinition(), aAssemblyIncluded));

            stringBuilder.Append("<");
            Type[]? xArgs = aType.GetGenericArguments();
            for (int i = 0; i < xArgs.Length - 1; i++)
            {
                stringBuilder.Append(GetFullName(xArgs[i], aAssemblyIncluded));
                stringBuilder.Append(", ");
            }

            stringBuilder.Append(GetFullName(xArgs.Last(), aAssemblyIncluded));
            stringBuilder.Append(">");
        }
        else
        {
            stringBuilder.Append(aType.FullName);
        }

        return stringBuilder.ToString();
    }

    /// <summary>
    /// Get the full name for the method
    /// </summary>
    /// <param name="aMethod"></param>
    /// <param name="aAssemblyIncluded">If true, id of assembly is included</param>
    /// <returns></returns>
    /// <exception cref="ArgumentNullException"></exception>
    public static string GetFullName(MethodBase aMethod, bool aAssemblyIncluded = true)
    {
        if (aMethod == null)
        {
            throw new ArgumentNullException(nameof(aMethod));
        }

        StringBuilder? xBuilder = new(256);
        string[]? xParts = aMethod.ToString().Split(' ');
        MethodInfo? xMethodInfo = aMethod as MethodInfo;
        if (xMethodInfo != null)
        {
            xBuilder.Append(GetFullName(xMethodInfo.ReturnType, aAssemblyIncluded));
        }
        else
        {
            ConstructorInfo? xCtor = aMethod as ConstructorInfo;
            if (xCtor != null)
            {
                xBuilder.Append(typeof(void).FullName);
            }
            else
            {
                xBuilder.Append(xParts[0]);
            }
        }

        xBuilder.Append("  ");
        if (aMethod.DeclaringType != null)
        {
            xBuilder.Append(GetFullName(aMethod.DeclaringType, aAssemblyIncluded));
        }
        else
        {
            xBuilder.Append("dynamic_method");
        }

        xBuilder.Append(".");
        if (aMethod.IsGenericMethod && !aMethod.IsGenericMethodDefinition)
        {
            xBuilder.Append(xMethodInfo.GetGenericMethodDefinition().Name);

            Type[]? xGenArgs = aMethod.GetGenericArguments();
            if (xGenArgs.Length > 0)
            {
                xBuilder.Append("<");
                for (int i = 0; i < xGenArgs.Length - 1; i++)
                {
                    xBuilder.Append(GetFullName(xGenArgs[i], aAssemblyIncluded));
                    xBuilder.Append(", ");
                }

                xBuilder.Append(GetFullName(xGenArgs.Last(), aAssemblyIncluded));
                xBuilder.Append(">");
            }
        }
        else
        {
            xBuilder.Append(aMethod.Name);
        }

        xBuilder.Append("(");
        ParameterInfo[]? xParams = aMethod.GetParameters();
        for (int i = 0; i < xParams.Length; i++)
        {
            if (i == 0 && xParams[i].Name == "aThis")
            {
                continue;
            }

            xBuilder.Append(GetFullName(xParams[i].ParameterType, aAssemblyIncluded));
            if (i < xParams.Length - 1)
            {
                xBuilder.Append(", ");
            }
        }

        xBuilder.Append(")");
        return xBuilder.ToString();
    }

    public static string GetFullName(FieldInfo aField) => GetFullName(aField.FieldType, false) + " " +
                                                          GetFullName(aField.DeclaringType, false) + "." + aField.Name;

    /// <summary>
    /// Gets a label for the given static field
    /// </summary>
    /// <param name="aType"></param>
    /// <param name="aField"></param>
    /// <returns></returns>
    /// <exception cref="NotSupportedException">throws if its not static</exception>
    public static string GetStaticFieldName(Type aType, string aField) => GetStaticFieldName(aType.GetField(aField));

    /// <summary>
    /// Gets a label for the given static field
    /// </summary>
    /// <param name="aField"></param>
    /// <returns></returns>
    /// <exception cref="NotSupportedException">throws if its not static</exception>
    public static string GetStaticFieldName(FieldInfo aField)
    {
        if (!aField.IsStatic)
        {
            throw new NotSupportedException($"{aField.Name}: is not static");
        }

        return FilterStringForIncorrectChars(
            "static_field__" + GetFullName(aField.DeclaringType) + "." + aField.Name);
    }

    public static string GetRandomLabel() => $"random_label__{Guid.NewGuid()}";
}
