#pragma once

#include <stddef.h>

#ifdef __cplusplus
#  define _TERM_EXTERN extern "C"
#else
#  define _TERM_EXTERN extern
#endif

_TERM_EXTERN void *term_alloc(size_t size);
_TERM_EXTERN void *term_realloc(void *oldptr, size_t size);
_TERM_EXTERN void term_free(void *ptr, size_t size);
_TERM_EXTERN void term_freensz(void *ptr);

_TERM_EXTERN void *term_memcpy(void *dest, const void *src, size_t size);
_TERM_EXTERN void *term_memset(void *dest, int val, size_t count);

#undef _TERM_EXTERN