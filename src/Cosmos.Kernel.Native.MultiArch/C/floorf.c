#include <stdint.h>

extern int isfinitef(float);

float floorf(float x) {
    if (!isfinitef(x))
        return x;

    union {
        float f;
        uint32_t u;
    } v = { x };

    uint32_t sign = v.u >> 31;
    int32_t exp = ((v.u >> 23) & 0xFF) - 127;

    // |x| < 1.0
    if (exp < 0) {
        if (v.u << 1 == 0)   // +-0.0
            return x;
        return sign ? -1.0f : 0.0f;
    }

    if (exp >= 23)
        return x;

    uint32_t mask = (1U << (23 - exp)) - 1;

    if ((v.u & mask) == 0)
        return x;

    if (sign)
        v.u += (1U << (23 - exp));

    v.u &= ~mask;

    return v.f;
}
