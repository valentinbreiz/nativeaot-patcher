#include <stdint.h>

extern int isfinite(double);

double floor(double x)
{
    if (!isfinite(x))
        return x;

    union {
        double f;
        uint64_t u;
    } v = { x };

    uint64_t sign = v.u >> 63;
    int32_t exp = ((v.u >> 52) & 0x7FF) - 1023;

    // |x| < 1.0
    if (exp < 0) {
        if ((v.u << 1) == 0)   // +- 0.0
            return x;
        return sign ? -1.0 : 0.0;
    }

    if (exp >= 52)
        return x;

    uint64_t mask = (1ULL << (52 - exp)) - 1;

    if ((v.u & mask) == 0)
        return x;

    if (sign)
        v.u += (1ULL << (52 - exp));

    v.u &= ~mask;

    return v.f;
}
