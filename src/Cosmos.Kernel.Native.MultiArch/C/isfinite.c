#include <stdint.h>

int isfinite(double x) {
    union {
        double d;
        uint64_t u;
    } v = { x };

    uint64_t exp = (v.u >> 52) & 0x7FFULL;

    return exp != 0x7FF;
}