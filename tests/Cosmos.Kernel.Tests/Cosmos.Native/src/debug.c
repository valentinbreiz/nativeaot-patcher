#include "debug.h"
#include "utils.h"
#include "framebuffer.h" // Include the framebuffer declaration
#include <flanterm/backends/fb.h>
#include <limine.h>

// Debug function to write text to the terminal
void debug_write(struct flanterm_context *ft_ctx, const char *message) {
    if (ft_ctx == NULL || message == NULL) {
        return; // Ensure the context and message are not NULL
    }
    size_t length = strlen(message); // Calculate the length of the string
    flanterm_write(ft_ctx, message, length); // Write the message to the terminal

    // Add a newline after the message
    const char *newline = "\n";
    flanterm_write(ft_ctx, newline, 1);
}

// Function to display Limine information
void display_limine_info(struct flanterm_context *ft_ctx) {
    // Display architecture
#if defined(__x86_64__)
    debug_write(ft_ctx, "Architecture: x86_64");
#elif defined(__aarch64__)
    debug_write(ft_ctx, "Architecture: ARM64 (AArch64)");
#elif defined(__riscv)
    debug_write(ft_ctx, "Architecture: RISC-V");
#elif defined(__loongarch64__)
    debug_write(ft_ctx, "Architecture: LoongArch64");
#else
    debug_write(ft_ctx, "Architecture: Unknown");
#endif

    // Display framebuffer information
    if (framebuffer_request.response != NULL && framebuffer_request.response->framebuffer_count > 0) {
        struct limine_framebuffer *framebuffer = framebuffer_request.response->framebuffers[0];
        char buffer[128];

        // Framebuffer resolution
        snprintf(buffer, sizeof(buffer), "Framebuffer: %ux%u, Pitch: %u",
                 framebuffer->width, framebuffer->height, framebuffer->pitch);
        debug_write(ft_ctx, buffer);

        // Framebuffer pixel format
        snprintf(buffer, sizeof(buffer), "Pixel format: R:%u:%u G:%u:%u B:%u:%u",
                 framebuffer->red_mask_size, framebuffer->red_mask_shift,
                 framebuffer->green_mask_size, framebuffer->green_mask_shift,
                 framebuffer->blue_mask_size, framebuffer->blue_mask_shift);
        debug_write(ft_ctx, buffer);
    } else {
        debug_write(ft_ctx, "Framebuffer: Not available");
    }
}