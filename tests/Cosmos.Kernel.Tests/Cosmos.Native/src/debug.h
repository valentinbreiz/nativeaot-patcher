#ifndef DEBUG_H
#define DEBUG_H

#include <stddef.h>
#include <flanterm/backends/fb.h>

void debug_write(struct flanterm_context *ft_ctx, const char *message);
void display_limine_info(struct flanterm_context *ft_ctx);

#endif