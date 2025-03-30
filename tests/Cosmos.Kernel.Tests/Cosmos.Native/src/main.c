#include <stdint.h>
#include <stddef.h>
#include <stdbool.h>
#include <limine.h>
#include <flanterm/backends/fb.h>
#include "debug.h"
#include "utils.h"
#include "framebuffer.h" // Include the header with the extern declaration

extern void dotnet_main(void);

// Limine framebuffer request
__attribute__((used, section(".limine_requests")))
volatile struct limine_framebuffer_request framebuffer_request = {
    .id = LIMINE_FRAMEBUFFER_REQUEST,
    .revision = 0
};

// Halt function (infinite loop)
static void hcf(void) {
    for (;;) {
#if defined(__x86_64__)
        asm("hlt");
#elif defined(__aarch64__) || defined(__riscv)
        asm("wfi");
#elif defined(__loongarch64)
        asm("idle 0");
#endif
    }
}

/* ------------------------
   Kernel entry point
   ------------------------ */
void kmain(void) {
    // Check if the framebuffer is available
    if (framebuffer_request.response == NULL || framebuffer_request.response->framebuffer_count < 1) {
        hcf();
    }

    // Retrieve the framebuffer
    struct limine_framebuffer *framebuffer = framebuffer_request.response->framebuffers[0];

    // Initialize the terminal
    struct flanterm_context *ft_ctx = flanterm_fb_init(
        NULL,
        NULL,
        framebuffer->address,
        framebuffer->width, framebuffer->height,
        framebuffer->pitch,
        framebuffer->red_mask_size, framebuffer->red_mask_shift,
        framebuffer->green_mask_size, framebuffer->green_mask_shift,
        framebuffer->blue_mask_size, framebuffer->blue_mask_shift,
        NULL,
        NULL, NULL,
        NULL, NULL,
        NULL, NULL,
        NULL, 0, 0, 1,
        0, 0,
        0
    );

    // Example of writing to the console with debug_write
    debug_write(ft_ctx, "CosmosOS Native Entry Point started!");

    debug_write(ft_ctx, "Limine info:");

    // Display Limine information
    display_limine_info(ft_ctx);

    debug_write(ft_ctx, "Jumping to C# Entry Point...");

    dotnet_main();

    debug_write(ft_ctx, "Returned to Native Entry Point!");

    // Infinite loop
    hcf();
}
