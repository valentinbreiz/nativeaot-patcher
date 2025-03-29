#ifndef UTILS_H
#define UTILS_H

#include <stddef.h>
#include <stdint.h>

void *memset(void *dest, int val, size_t count);
void *memcpy(void *dest, const void *src, size_t count);
size_t strlen(const char *str);
int snprintf(char *buffer, size_t size, const char *format, ...);

#endif