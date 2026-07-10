/* @(#)fdlibm.h 1.5 04/04/22 */
/*
 * ====================================================
 * Copyright (C) 2004 by Sun Microsystems, Inc. All rights reserved.
 *
 * Permission to use, copy, modify, and distribute this
 * software is freely granted, provided that this notice
 * is preserved.
 * ====================================================
 */

// Transcendental math functions.
// Core functions (sin, cos, tan, exp, log, atan): ARM64 uses C# fdlibm,
//   x64 uses x87 FPU assembly in Cosmos.Kernel.Native.X64/Runtime/Runtime.s.
// Derived functions (asin, acos, atan2, pow, log2, log10): shared C# on both arches,
//   ported from fdlibm via Cosmos Gen2 (Cosmos/source/Cosmos.System2_Plugs/System/MathImpl.cs).

using System.Runtime;
using System.Runtime.CompilerServices;

namespace Cosmos.Kernel.Core.Runtime;

internal static class Math
{
    // =========================================================================
    // Shared helpers
    // =========================================================================

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe long DoubleToBits(double d)
    {
        return *(long*)&d;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe double BitsToDouble(long bits)
    {
        return *(double*)&bits;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double Abs(double x)
    {
        return x < 0 ? -x : x;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double RoundToNearest(double x)
    {
        return (double)(long)(x + (x >= 0 ? 0.5 : -0.5));
    }

    // IEEE 754 bit manipulation helpers — used by fdlibm functions on both arches
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe int HighWord(double x)
    {
        long bits = *(long*)&x;
        return (int)(bits >> 32);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe int LowWord(double x)
    {
        long bits = *(long*)&x;
        return (int)bits;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe double SetHighWord(double x, int hi)
    {
        long bits = *(long*)&x;
        bits = (bits & 0x00000000FFFFFFFFL) | ((long)hi << 32);
        return *(double*)&bits;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe double SetLowWord(double x, int lo)
    {
        long bits = *(long*)&x;
        bits = (bits & unchecked((long)0xFFFFFFFF00000000L)) | ((long)lo & 0xFFFFFFFFL);
        return *(double*)&bits;
    }

    // =========================================================================
    // Shared constants
    // =========================================================================

    private const double PI = 3.14159265358979323846;
    private const double PI_OVER_2 = 1.57079632679489661923;
    // Low word of the pi/2 double-double split (fdlibm pio2_lo, shared by asin/acos).
    private const double PI_OVER_2_LO = 6.12323399573676603587e-17; /* 0x3C91A626, 0x33145C07 */
    private const double PI_OVER_4 = 0.78539816339744830962;
    private const double LN2 = 0.69314718055994530942;
    // Hi/lo extended-precision split of ln 2 (fdlibm ln2_hi/ln2_lo, shared by exp/log);
    // deliberately distinct from the full-precision LN2 above.
    private const double LN2_HI = 6.93147180369123816490e-01; /* 0x3FE62E42, 0xFEE00000 */
    private const double LN2_LO = 1.90821492927058770002e-10; /* 0x3DEA39EF, 0x35793C76 */
    private const double LOG2_E = 1.44269504088896340736; /* 0x3FF71547, 0x652B82FE */
    private const double LOG10_E = 0.43429448190325182765;

    // =========================================================================
    // Unconditional functions (both arches use C#)
    // =========================================================================

    [RuntimeExport("ceil")]
    internal static double ceil(double x)
    {
        if (x == (long)x)
        {
            return x;
        }

        if (double.IsNaN(x))
        {
            return double.NaN;
        }

        if (double.IsPositiveInfinity(x))
        {
            return double.PositiveInfinity;
        }

        if (double.IsNegativeInfinity(x))
        {
            return double.NegativeInfinity;
        }

        if (x > 0)
        {
            return (long)x + 1;
        }

        return (long)x;
    }

    [RuntimeExport("ceilf")]
    internal static float ceilf(float x)
    {
        return (float)ceil(x);
    }

    [RuntimeExport("sqrt")]
    internal static double sqrt(double x)
    {
        if (double.IsNaN(x) || x < 0)
        {
            return double.NaN;
        }

        if (double.IsPositiveInfinity(x))
        {
            return double.PositiveInfinity;
        }

        if (x == 0)
        {
            return 0;
        }

        double guess = x;
        double epsilon = 1e-10;

        for (int i = 0; i < 50; i++)
        {
            double nextGuess = (guess + x / guess) / 2.0;
            if (Abs(nextGuess - guess) < epsilon)
            {
                break;
            }

            guess = nextGuess;
        }

        return guess;
    }

    [RuntimeExport("sqrtf")]
    internal static float sqrtf(float x)
    {
        return (float)sqrt(x);
    }

    [RuntimeExport("trunc")]
    internal static double trunc(double x)
    {
        if (double.IsNaN(x))
        {
            return double.NaN;
        }

        if (double.IsInfinity(x))
        {
            return x;
        }

        return (long)x;
    }

    [RuntimeExport("truncf")]
    internal static float truncf(float x)
    {
        return (float)trunc(x);
    }

    [RuntimeExport("modf")]
    internal static unsafe double ModF(double x, double* intptr)
    {
        if (double.IsNaN(x))
        {
            *intptr = double.NaN;
            return double.NaN;
        }

        if (double.IsInfinity(x))
        {
            *intptr = x;
            return 0.0;
        }

        long intPart = (long)x;
        *intptr = (double)intPart;
        return x - intPart;
    }

    [RuntimeExport("fma")]
    internal static double fma(double x, double y, double z)
    {
        return x * y + z;
    }

    [RuntimeExport("fmaf")]
    internal static float fmaf(float x, float y, float z)
    {
        return x * y + z;
    }

    // =========================================================================
    // Core fdlibm implementations (private, no RuntimeExport)
    // Used by shared derived functions on both arches.
    // On ARM64, also RuntimeExported below.
    // On x64, core symbols come from x87 asm; these C# versions serve only
    // as internal helpers for the shared derived functions.
    // =========================================================================

    // --------------- exp (fdlibm e_exp.c) ---------------

    private static double FdlibmExp(double x)
    {
        const double o_threshold = 7.09782712893383973096e+02;  /* 0x40862E42, 0xFEFA39EF */
        const double u_threshold = -7.45133219101941108420e+02; /* 0xC0874910, 0xD52D3051 */
        const double twom1000 = 9.33263618503218878990e-302;    /* 2^-1000 */
        const double P1 = 1.66666666666666019037e-01;           /* 0x3FC55555, 0x5555553E */
        const double P2 = -2.77777777770155933842e-03;          /* 0xBF66C16C, 0x16BEBD93 */
        const double P3 = 6.61375632143793436117e-05;           /* 0x3F11566A, 0xAF25DE2C */
        const double P4 = -1.65339022054652515390e-06;          /* 0xBEBBBD41, 0xC5D26BF1 */
        const double P5 = 4.13813679705723846039e-08;           /* 0x3E663769, 0x72BEA4D0 */
        const double huge = 1.0e+300;

        double y, hi = 0, lo = 0, t, c;
        int k = 0;

        int hx = HighWord(x);
        int xsb = (hx >> 31) & 1;
        hx &= 0x7fffffff;

        if (hx >= 0x40862E42)
        {
            if (hx >= 0x7ff00000)
            {
                if (((hx & 0xfffff) | LowWord(x)) != 0)
                {
                    return x;
                }

                return xsb == 0 ? x : 0.0;
            }

            if (x > o_threshold)
            {
                return double.PositiveInfinity;
            }

            if (x < u_threshold)
            {
                return 0;
            }
        }

        if (hx > 0x3fd62e42)
        {
            if (hx < 0x3FF0A2B2)
            {
                if (xsb == 0)
                {
                    hi = x - LN2_HI;
                    lo = LN2_LO;
                }
                else
                {
                    hi = x + LN2_HI;
                    lo = -LN2_LO;
                }

                k = 1 - xsb - xsb;
            }
            else
            {
                k = (int)(LOG2_E * x + (xsb == 0 ? 0.5 : -0.5));
                t = k;
                hi = x - t * LN2_HI;
                lo = t * LN2_LO;
            }

            x = hi - lo;
        }
        else if (hx < 0x3e300000)
        {
            if (huge + x > 1)
            {
                return 1 + x;
            }
        }
        else
        {
            k = 0;
        }

        t = x * x;
        c = x - t * (P1 + t * (P2 + t * (P3 + t * (P4 + t * P5))));

        if (k == 0)
        {
            return 1 - (x * c / (c - 2.0) - x);
        }
        else
        {
            y = 1 - (lo - x * c / (2.0 - c) - hi);
        }

        if (k >= -1021)
        {
            long _y = DoubleToBits(y);
            _y += (long)k << 52;
            return BitsToDouble(_y);
        }
        else
        {
            long _y = DoubleToBits(y);
            _y += ((long)k + 1000) << 52;
            return BitsToDouble(_y) * twom1000;
        }
    }

    // --------------- log (fdlibm e_log.c) ---------------

    private static double FdlibmLog(double x)
    {
        const double two54 = 1.80143985094819840000e+16;
        const double Lg1 = 6.666666666666735130e-01;
        const double Lg2 = 3.999999999940941908e-01;
        const double Lg3 = 2.857142874366239149e-01;
        const double Lg4 = 2.222219843214978396e-01;
        const double Lg5 = 1.818357216161805012e-01;
        const double Lg6 = 1.531383769920937332e-01;
        const double Lg7 = 1.479819860511658591e-01;

        double hfsq, R, dk;

        int hx = HighWord(x);
        uint lx = (uint)LowWord(x);

        int k = 0;
        if (hx < 0x00100000)
        {
            if (x < 0 || double.IsNaN(x))
            {
                return double.NaN;
            }

            if (((hx & 0x7fffffff) | (int)lx) == 0)
            {
                return double.NegativeInfinity;
            }

            k -= 54;
            x *= two54;
            hx = HighWord(x);
        }

        if (hx >= 0x7ff00000)
        {
            return x + x;
        }

        k += (hx >> 20) - 1023;
        hx &= 0x000fffff;
        int i = (hx + 0x95f64) & 0x100000;
        x = SetHighWord(x, hx | (i ^ 0x3ff00000));
        k += i >> 20;
        double f = x - 1.0;

        if ((0x000fffff & (2 + hx)) < 3)
        {
            if (f == 0)
            {
                if (k == 0)
                {
                    return 0;
                }

                dk = k;
                return dk * LN2_HI + dk * LN2_LO;
            }

            R = f * f * (0.5 - 0.33333333333333333 * f);
            if (k == 0)
            {
                return f - R;
            }

            dk = k;
            return dk * LN2_HI - (R - dk * LN2_LO - f);
        }

        double s = f / (2.0 + f);
        dk = k;
        double z = s * s;
        i = hx - 0x6147a;
        double w = z * z;
        int j = 0x6b851 - hx;
        double t1 = w * (Lg2 + w * (Lg4 + w * Lg6));
        double t2 = z * (Lg1 + w * (Lg3 + w * (Lg5 + w * Lg7)));
        i |= j;
        R = t2 + t1;

        if (i > 0)
        {
            hfsq = 0.5 * f * f;
            if (k == 0)
            {
                return f - (hfsq - s * (hfsq + R));
            }

            return dk * LN2_HI - (hfsq - (s * (hfsq + R) + dk * LN2_LO) - f);
        }
        else
        {
            if (k == 0)
            {
                return f - s * (f - R);
            }

            return dk * LN2_HI - (s * (f - R) - dk * LN2_LO - f);
        }
    }

    // --------------- atan (fdlibm s_atan.c) ---------------

    private const double atanhi_0 = 4.63647609000806093515e-01; /* 0x3FDDAC67, 0x0561BB4F */
    private const double atanhi_1 = 7.85398163397448278999e-01; /* 0x3FE921FB, 0x54442D18 */
    private const double atanhi_2 = 9.82793723247329054082e-01; /* 0x3FEF730B, 0xD281F69B */
    private const double atanhi_3 = PI_OVER_2;                  /* 0x3FF921FB, 0x54442D18 */
    private const double atanlo_0 = 2.26987774529616870924e-17; /* 0x3C7A2B7F, 0x222F65E2 */
    private const double atanlo_1 = 3.06161699786838301793e-17; /* 0x3C81A626, 0x33145C07 */
    private const double atanlo_2 = 1.39033110312309984516e-17; /* 0x3C700788, 0x7AF0CBBD */
    private const double atanlo_3 = 6.12323399573676603587e-17; /* 0x3C91A626, 0x33145C07 */
    private const double aT_0 = 3.33333333333329318027e-01;     /* 0x3FD55555, 0x5555550D */
    private const double aT_1 = -1.99999999998764832476e-01;    /* 0xBFC99999, 0x9998EBC4 */
    private const double aT_2 = 1.42857142725034663711e-01;     /* 0x3FC24924, 0x920083FF */
    private const double aT_3 = -1.11111104054623557880e-01;    /* 0xBFBC71C6, 0xFE231671 */
    private const double aT_4 = 9.09088713343650656196e-02;     /* 0x3FB745CD, 0xC54C206E */
    private const double aT_5 = -7.69187620504482999495e-02;    /* 0xBFB3B0F2, 0xAF749A6D */
    private const double aT_6 = 6.66107313738753120669e-02;     /* 0x3FB10D66, 0xA0D03D51 */
    private const double aT_7 = -5.83357013379057348645e-02;    /* 0xBFADDE2D, 0x52DEFD9A */
    private const double aT_8 = 4.97687799461593236017e-02;     /* 0x3FA97B4B, 0x24760DEB */
    private const double aT_9 = -3.65315727442169155270e-02;    /* 0xBFA2B444, 0x2C6A6C2F */
    private const double aT_10 = 1.62858201153657823623e-02;    /* 0x3F90AD3A, 0xE322DA11 */

    private static double FdlibmAtan(double x)
    {
        if (double.IsNaN(x))
        {
            return double.NaN;
        }

        int id;
        int hx = HighWord(x);
        int ix = hx & 0x7fffffff;

        if (ix >= 0x44100000)
        {
            if (ix > 0x7ff00000 || (ix == 0x7ff00000 && LowWord(x) != 0))
            {
                return x + x;
            }

            if (hx > 0)
            {
                return atanhi_3 + atanlo_3;
            }

            return -atanhi_3 - atanlo_3;
        }

        if (ix < 0x3fdc0000)
        {
            if (ix < 0x3e200000)
            {
                if (1.0e+300 + x > 1)
                {
                    return x;
                }
            }

            id = -1;
        }
        else
        {
            x = Abs(x);
            if (ix < 0x3ff30000)
            {
                if (ix < 0x3fe60000)
                {
                    id = 0;
                    x = (2.0 * x - 1) / (2.0 + x);
                }
                else
                {
                    id = 1;
                    x = (x - 1) / (x + 1);
                }
            }
            else
            {
                if (ix < 0x40038000)
                {
                    id = 2;
                    x = (x - 1.5) / (1 + 1.5 * x);
                }
                else
                {
                    id = 3;
                    x = -1.0 / x;
                }
            }
        }

        double z = x * x;
        double w = z * z;
        double s1 = z * (aT_0 + w * (aT_2 + w * (aT_4 + w * (aT_6 + w * (aT_8 + w * aT_10)))));
        double s2 = w * (aT_1 + w * (aT_3 + w * (aT_5 + w * (aT_7 + w * aT_9))));

        if (id < 0)
        {
            return x - x * (s1 + s2);
        }

        double ahi, alo;
        switch (id)
        {
            case 0: ahi = atanhi_0; alo = atanlo_0; break;
            case 1: ahi = atanhi_1; alo = atanlo_1; break;
            case 2: ahi = atanhi_2; alo = atanlo_2; break;
            default: ahi = atanhi_3; alo = atanlo_3; break;
        }

        z = ahi - (x * (s1 + s2) - alo - x);
        return hx < 0 ? -z : z;
    }

    // =========================================================================
    // ARM64-only: core transcendental RuntimeExports
    // On x64 these symbols come from x87 FPU assembly.
    // =========================================================================

#if ARCH_ARM64

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double SinCore(double x)
    {
        double x2 = x * x;
        double term = x;
        double sum = x;

        term *= -x2 / (2.0 * 3.0);     sum += term;
        term *= -x2 / (4.0 * 5.0);     sum += term;
        term *= -x2 / (6.0 * 7.0);     sum += term;
        term *= -x2 / (8.0 * 9.0);     sum += term;
        term *= -x2 / (10.0 * 11.0);   sum += term;
        term *= -x2 / (12.0 * 13.0);   sum += term;
        term *= -x2 / (14.0 * 15.0);   sum += term;
        term *= -x2 / (16.0 * 17.0);   sum += term;

        return sum;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double CosCore(double x)
    {
        double x2 = x * x;
        double term = 1.0;
        double sum = 1.0;

        term *= -x2 / (1.0 * 2.0);     sum += term;
        term *= -x2 / (3.0 * 4.0);     sum += term;
        term *= -x2 / (5.0 * 6.0);     sum += term;
        term *= -x2 / (7.0 * 8.0);     sum += term;
        term *= -x2 / (9.0 * 10.0);    sum += term;
        term *= -x2 / (11.0 * 12.0);   sum += term;
        term *= -x2 / (13.0 * 14.0);   sum += term;
        term *= -x2 / (15.0 * 16.0);   sum += term;

        return sum;
    }

    [RuntimeExport("sin")]
    internal static double sin(double x)
    {
        if (double.IsNaN(x) || double.IsInfinity(x))
        {
            return double.NaN;
        }

        double sign = 1.0;
        if (x < 0)
        {
            sign = -1.0;
            x = -x;
        }

        double j = RoundToNearest(x / PI_OVER_2);
        double r = x - j * PI_OVER_2;
        int q = (int)j & 3;

        double result;
        if (q == 0)
        {
            result = SinCore(r);
        }
        else if (q == 1)
        {
            result = CosCore(r);
        }
        else if (q == 2)
        {
            result = -SinCore(r);
        }
        else
        {
            result = -CosCore(r);
        }

        return sign * result;
    }

    [RuntimeExport("sinf")]
    internal static float sinf(float x) => (float)sin(x);

    [RuntimeExport("cos")]
    internal static double cos(double x)
    {
        if (double.IsNaN(x) || double.IsInfinity(x))
        {
            return double.NaN;
        }

        x = x < 0 ? -x : x;

        double j = RoundToNearest(x / PI_OVER_2);
        double r = x - j * PI_OVER_2;
        int q = (int)j & 3;

        if (q == 0)
        {
            return CosCore(r);
        }

        if (q == 1)
        {
            return -SinCore(r);
        }

        if (q == 2)
        {
            return -CosCore(r);
        }

        return SinCore(r);
    }

    [RuntimeExport("cosf")]
    internal static float cosf(float x) => (float)cos(x);

    [RuntimeExport("tan")]
    internal static double tan(double x)
    {
        return sin(x) / cos(x);
    }

    [RuntimeExport("tanf")]
    internal static float tanf(float x) => (float)tan(x);

    [RuntimeExport("exp")]
    internal static double exp(double x) => FdlibmExp(x);

    [RuntimeExport("expf")]
    internal static float expf(float x) => (float)FdlibmExp(x);

    [RuntimeExport("log")]
    internal static double log(double x) => FdlibmLog(x);

    [RuntimeExport("logf")]
    internal static float logf(float x) => (float)FdlibmLog(x);

    [RuntimeExport("atan")]
    internal static double atan(double x) => FdlibmAtan(x);

    [RuntimeExport("atanf")]
    internal static float atanf(float x) => (float)FdlibmAtan(x);

#endif

    // =========================================================================
    // Shared derived functions (both arches use C#)
    // These call FdlibmExp/FdlibmLog/FdlibmAtan internally.
    // =========================================================================

    // --------------- log2 ---------------

    [RuntimeExport("log2")]
    internal static double log2(double x)
    {
        double ln = FdlibmLog(x);
        if (double.IsNaN(ln) || double.IsInfinity(ln))
        {
            return ln;
        }

        return ln / LN2;
    }

    [RuntimeExport("log2f")]
    internal static float log2f(float x) => (float)log2(x);

    // --------------- log10 ---------------

    [RuntimeExport("log10")]
    internal static double log10(double x)
    {
        double ln = FdlibmLog(x);
        if (double.IsNaN(ln) || double.IsInfinity(ln))
        {
            return ln;
        }

        return ln * LOG10_E;
    }

    [RuntimeExport("log10f")]
    internal static float log10f(float x) => (float)log10(x);

    // --------------- atan2 ---------------

    [RuntimeExport("atan2")]
    internal static double atan2(double y, double x)
    {
        if (double.IsNaN(x) || double.IsNaN(y))
        {
            return double.NaN;
        }

        if (double.IsPositiveInfinity(y))
        {
            if (double.IsPositiveInfinity(x))
            {
                return PI_OVER_4;
            }

            if (double.IsNegativeInfinity(x))
            {
                return 3.0 * PI_OVER_4;
            }

            return PI_OVER_2;
        }

        if (double.IsNegativeInfinity(y))
        {
            if (double.IsPositiveInfinity(x))
            {
                return -PI_OVER_4;
            }

            if (double.IsNegativeInfinity(x))
            {
                return -3.0 * PI_OVER_4;
            }

            return -PI_OVER_2;
        }

        if (double.IsPositiveInfinity(x))
        {
            return 0.0;
        }

        if (double.IsNegativeInfinity(x))
        {
            return y >= 0 ? PI : -PI;
        }

        if (x == 0)
        {
            if (y > 0)
            {
                return PI_OVER_2;
            }

            if (y < 0)
            {
                return -PI_OVER_2;
            }

            return 0.0;
        }

        double a = FdlibmAtan(y / x);

        if (x < 0)
        {
            return y >= 0 ? a + PI : a - PI;
        }

        return a;
    }

    [RuntimeExport("atan2f")]
    internal static float atan2f(float y, float x) => (float)atan2(y, x);

    // --------------- asin/acos shared coefficients ---------------
    // Rational approximation R(x^2) of fdlibm e_asin.c, reused verbatim by e_acos.c.

    private const double pS0 = 1.66666666666666657415e-01;
    private const double pS1 = -3.25565818622400915405e-01;
    private const double pS2 = 2.01212532134862925881e-01;
    private const double pS3 = -4.00555345006794114027e-02;
    private const double pS4 = 7.91534994289814532176e-04;
    private const double pS5 = 3.47933107596021167570e-05;
    private const double qS1 = -2.40339491173441421878e+00;
    private const double qS2 = 2.02094576023350569471e+00;
    private const double qS3 = -6.88283971605453293030e-01;
    private const double qS4 = 7.70381505559019352791e-02;

    // --------------- asin (fdlibm e_asin.c) ---------------

    [RuntimeExport("asin")]
    internal static double asin(double x)
    {
        const double huge = 1.000e+300;
        const double pio2_hi = PI_OVER_2;

        double t = 0, w, p, q, c, r, s;
        int hx = HighWord(x);
        int ix = hx & 0x7fffffff;

        if (ix >= 0x3ff00000)
        {
            if (((ix - 0x3ff00000) | LowWord(x)) == 0)
            {
                return x * pio2_hi + x * PI_OVER_2_LO;
            }

            return (x - x) / (x - x);
        }
        else if (ix < 0x3fe00000)
        {
            if (ix < 0x3e400000)
            {
                if (huge + x > 1)
                {
                    return x;
                }
            }
            else
            {
                t = x * x;
            }

            p = t * (pS0 + t * (pS1 + t * (pS2 + t * (pS3 + t * (pS4 + t * pS5)))));
            q = 1 + t * (qS1 + t * (qS2 + t * (qS3 + t * qS4)));
            w = p / q;
            return x + x * w;
        }

        w = 1 - Abs(x);
        t = w * 0.5;
        p = t * (pS0 + t * (pS1 + t * (pS2 + t * (pS3 + t * (pS4 + t * pS5)))));
        q = 1 + t * (qS1 + t * (qS2 + t * (qS3 + t * qS4)));
        s = sqrt(t);

        if (ix >= 0x3FEF3333)
        {
            w = p / q;
            t = pio2_hi - (2.0 * (s + s * w) - PI_OVER_2_LO);
        }
        else
        {
            w = s;
            w = SetLowWord(w, 0);
            c = (t - w * w) / (s + w);
            r = p / q;
            p = 2.0 * s * r - (PI_OVER_2_LO - 2.0 * c);
            q = PI_OVER_4 - 2.0 * w;
            t = PI_OVER_4 - (p - q);
        }

        return hx > 0 ? t : -t;
    }

    [RuntimeExport("asinf")]
    internal static float asinf(float x) => (float)asin(x);

    // --------------- acos (fdlibm e_acos.c) ---------------

    [RuntimeExport("acos")]
    internal static double acos(double x)
    {
        const double pio2_hi = PI_OVER_2;

        double z, p, q, r, w, s, c, df;
        int hx = HighWord(x);
        int ix = hx & 0x7fffffff;

        if (ix >= 0x3ff00000)
        {
            if (((ix - 0x3ff00000) | LowWord(x)) == 0)
            {
                if (hx > 0)
                {
                    return 0.0;
                }

                return PI + 2.0 * PI_OVER_2_LO;
            }

            return (x - x) / (x - x);
        }

        if (ix < 0x3fe00000)
        {
            if (ix <= 0x3c600000)
            {
                return pio2_hi + PI_OVER_2_LO;
            }

            z = x * x;
            p = z * (pS0 + z * (pS1 + z * (pS2 + z * (pS3 + z * (pS4 + z * pS5)))));
            q = 1 + z * (qS1 + z * (qS2 + z * (qS3 + z * qS4)));
            r = p / q;
            return pio2_hi - (x - (PI_OVER_2_LO - x * r));
        }
        else if (hx < 0)
        {
            z = (1 + x) * 0.5;
            p = z * (pS0 + z * (pS1 + z * (pS2 + z * (pS3 + z * (pS4 + z * pS5)))));
            q = 1 + z * (qS1 + z * (qS2 + z * (qS3 + z * qS4)));
            s = sqrt(z);
            r = p / q;
            w = r * s - PI_OVER_2_LO;
            return PI - 2.0 * (s + w);
        }
        else
        {
            z = (1 - x) * 0.5;
            s = sqrt(z);
            df = s;
            df = SetLowWord(df, 0);
            c = (z - df * df) / (s + df);
            p = z * (pS0 + z * (pS1 + z * (pS2 + z * (pS3 + z * (pS4 + z * pS5)))));
            q = 1 + z * (qS1 + z * (qS2 + z * (qS3 + z * qS4)));
            r = p / q;
            w = r * s + c;
            return 2.0 * (df + w);
        }
    }

    [RuntimeExport("acosf")]
    internal static float acosf(float x) => (float)acos(x);

    // --------------- pow ---------------

    [RuntimeExport("pow")]
    internal static double pow(double x, double y)
    {
        if (y == 0.0)
        {
            return 1.0;
        }

        if (x == 1.0)
        {
            return 1.0;
        }

        if (double.IsNaN(x) || double.IsNaN(y))
        {
            return double.NaN;
        }

        if (double.IsNegativeInfinity(x))
        {
            if (y < 0)
            {
                return 0;
            }

            if ((long)y % 2 == 0)
            {
                return double.PositiveInfinity;
            }

            return double.NegativeInfinity;
        }

        if (double.IsPositiveInfinity(x))
        {
            return y < 0 ? 0.0 : double.PositiveInfinity;
        }

        if (double.IsInfinity(y))
        {
            double absX = Abs(x);
            if (absX < 1)
            {
                return double.IsPositiveInfinity(y) ? 0.0 : double.PositiveInfinity;
            }

            if (absX > 1)
            {
                return double.IsPositiveInfinity(y) ? double.PositiveInfinity : 0.0;
            }

            return double.NaN;
        }

        if (x == 0.0)
        {
            return y > 0 ? 0.0 : double.PositiveInfinity;
        }

        /* Integer exponent fast path — exact results for small n */
        if (y == trunc(y) && y >= -64 && y <= 64)
        {
            double absX = x < 0 ? -x : x;
            long n = (long)y;
            bool negate = false;
            if (n < 0)
            {
                n = -n;
                negate = true;
            }

            double result = 1.0;
            double b = absX;
            while (n > 0)
            {
                if ((n & 1) != 0)
                {
                    result *= b;
                }

                b *= b;
                n >>= 1;
            }

            if (negate)
            {
                result = 1.0 / result;
            }

            if (x < 0 && ((long)y & 1) != 0)
            {
                result = -result;
            }

            return result;
        }

        if (x < 0)
        {
            if (y != trunc(y))
            {
                return double.NaN;
            }

            double result = FdlibmExp(y * FdlibmLog(-x));
            return ((long)y & 1) != 0 ? -result : result;
        }

        return FdlibmExp(y * FdlibmLog(x));
    }

    [RuntimeExport("powf")]
    internal static float powf(float x, float y) => (float)pow(x, y);
}
