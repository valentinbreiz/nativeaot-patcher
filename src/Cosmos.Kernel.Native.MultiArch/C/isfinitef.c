#include <stdint.h>

int isfinitef(float x) {
    union {
        float f;
        uint32_t u;
    } v = { x };

    uint32_t exp = (v.u >> 23) & 0xFFU;

    return exp != 0xFF;
}