using Cosmos.Build.API.Attributes;

namespace Cosmos.Kernel.Plugs.System.Runtime.Intrinsics.X86;

/// <summary>
/// Plugs for X86-specific intrinsics to prevent crashes on ARM64.
/// NativeAOT ARM64 has codegen bugs where it tries to use X86 SIMD instructions.
/// These plugs ensure IsSupported always returns false on ARM64.
/// </summary>

[Plug("System.Runtime.Intrinsics.X86.Avx512BW")]
public static class Avx512BWPlug
{
    [PlugMember("get_IsSupported")]
    public static bool get_IsSupported() => false;
}

[Plug("System.Runtime.Intrinsics.X86.Avx512F")]
public static class Avx512FPlug
{
    [PlugMember("get_IsSupported")]
    public static bool get_IsSupported() => false;
}

[Plug("System.Runtime.Intrinsics.X86.Avx512DQ")]
public static class Avx512DQPlug
{
    [PlugMember("get_IsSupported")]
    public static bool get_IsSupported() => false;
}

[Plug("System.Runtime.Intrinsics.X86.Avx512CD")]
public static class Avx512CDPlug
{
    [PlugMember("get_IsSupported")]
    public static bool get_IsSupported() => false;
}

[Plug("System.Runtime.Intrinsics.X86.Avx512Vbmi")]
public static class Avx512VbmiPlug
{
    [PlugMember("get_IsSupported")]
    public static bool get_IsSupported() => false;
}

[Plug("System.Runtime.Intrinsics.X86.Avx")]
public static class AvxPlug
{
    [PlugMember("get_IsSupported")]
    public static bool get_IsSupported() => false;
}

[Plug("System.Runtime.Intrinsics.X86.Avx2")]
public static class Avx2Plug
{
    [PlugMember("get_IsSupported")]
    public static bool get_IsSupported() => false;
}

[Plug("System.Runtime.Intrinsics.X86.Avx10v1")]
public static class Avx10v1Plug
{
    [PlugMember("get_IsSupported")]
    public static bool get_IsSupported() => false;
}

[Plug("System.Runtime.Intrinsics.X86.AvxVnni")]
public static class AvxVnniPlug
{
    [PlugMember("get_IsSupported")]
    public static bool get_IsSupported() => false;
}

[Plug("System.Runtime.Intrinsics.X86.Sse")]
public static class SsePlug
{
    [PlugMember("get_IsSupported")]
    public static bool get_IsSupported() => false;
}

[Plug("System.Runtime.Intrinsics.X86.Sse2")]
public static class Sse2Plug
{
    [PlugMember("get_IsSupported")]
    public static bool get_IsSupported() => false;
}

[Plug("System.Runtime.Intrinsics.X86.Sse3")]
public static class Sse3Plug
{
    [PlugMember("get_IsSupported")]
    public static bool get_IsSupported() => false;
}

[Plug("System.Runtime.Intrinsics.X86.Ssse3")]
public static class Ssse3Plug
{
    [PlugMember("get_IsSupported")]
    public static bool get_IsSupported() => false;
}

[Plug("System.Runtime.Intrinsics.X86.Sse41")]
public static class Sse41Plug
{
    [PlugMember("get_IsSupported")]
    public static bool get_IsSupported() => false;
}

[Plug("System.Runtime.Intrinsics.X86.Sse42")]
public static class Sse42Plug
{
    [PlugMember("get_IsSupported")]
    public static bool get_IsSupported() => false;
}

[Plug("System.Runtime.Intrinsics.X86.Aes")]
public static class AesPlug
{
    [PlugMember("get_IsSupported")]
    public static bool get_IsSupported() => false;
}

[Plug("System.Runtime.Intrinsics.X86.Bmi1")]
public static class Bmi1Plug
{
    [PlugMember("get_IsSupported")]
    public static bool get_IsSupported() => false;
}

[Plug("System.Runtime.Intrinsics.X86.Bmi2")]
public static class Bmi2Plug
{
    [PlugMember("get_IsSupported")]
    public static bool get_IsSupported() => false;
}

[Plug("System.Runtime.Intrinsics.X86.Fma")]
public static class FmaPlug
{
    [PlugMember("get_IsSupported")]
    public static bool get_IsSupported() => false;
}

[Plug("System.Runtime.Intrinsics.X86.Lzcnt")]
public static class LzcntPlug
{
    [PlugMember("get_IsSupported")]
    public static bool get_IsSupported() => false;
}

[Plug("System.Runtime.Intrinsics.X86.Pclmulqdq")]
public static class PclmulqdqPlug
{
    [PlugMember("get_IsSupported")]
    public static bool get_IsSupported() => false;
}

[Plug("System.Runtime.Intrinsics.X86.Popcnt")]
public static class PopcntPlug
{
    [PlugMember("get_IsSupported")]
    public static bool get_IsSupported() => false;
}

[Plug("System.Runtime.Intrinsics.X86.X86Base")]
public static class X86BasePlug
{
    [PlugMember("get_IsSupported")]
    public static bool get_IsSupported() => false;
}

[Plug("System.Runtime.Intrinsics.X86.X86Serialize")]
public static class X86SerializePlug
{
    [PlugMember("get_IsSupported")]
    public static bool get_IsSupported() => false;
}
