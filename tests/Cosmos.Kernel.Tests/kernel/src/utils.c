#include "utils.h"
#include <stdarg.h> // Include stdarg.h for va_list, va_start, va_arg, va_end

// Implementation of memset
void *memset(void *dest, int val, size_t count) {
    uint8_t *d = (uint8_t *)dest;
    for (size_t i = 0; i < count; i++) {
        d[i] = (uint8_t)val;
    }
    return dest;
}

// Implementation of memcpy
void *memcpy(void *dest, const void *src, size_t count) {
    uint8_t *d = (uint8_t *)dest;
    const uint8_t *s = (const uint8_t *)src;
    for (size_t i = 0; i < count; i++) {
        d[i] = s[i];
    }
    return dest;
}

// Implementation of strlen
size_t strlen(const char *str) {
    size_t len = 0;
    while (str[len] != '\0') {
        len++;
    }
    return len;
}

// Minimal implementation of snprintf
int snprintf(char *buffer, size_t size, const char *format, ...) {
    va_list args;
    va_start(args, format);

    // Pointer to traverse the buffer
    char *buf_ptr = buffer;
    size_t written = 0;

    while (*format && written < size - 1) {
        if (*format == '%' && *(format + 1) == 'u') {
            // Handle unsigned integer
            format += 2;
            unsigned int value = va_arg(args, unsigned int);
            char num_buffer[20];
            int num_len = 0;

            // Convert number to string
            do {
                num_buffer[num_len++] = '0' + (value % 10);
                value /= 10;
            } while (value > 0);

            // Write the number in reverse order
            while (num_len > 0 && written < size - 1) {
                *buf_ptr++ = num_buffer[--num_len];
                written++;
            }
        } else {
            // Copy regular characters
            *buf_ptr++ = *format++;
            written++;
        }
    }

    // Null-terminate the buffer
    *buf_ptr = '\0';

    va_end(args);
    return written;
}