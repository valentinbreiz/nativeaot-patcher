/*
 * Cosmos Debug Buffer
 *
 * This buffer is placed in the .cosmos_debug linker section
 * and used for live kernel instrumentation without pausing execution.
 */

// Place buffer in dedicated linker section
__attribute__((section(".cosmos_debug")))
__attribute__((aligned(4096)))
unsigned char cosmos_debug_buffer[4096] = {0};

// Export function to get buffer address (callable from C#)
void* __cosmos_get_debug_buffer(void) {
    return cosmos_debug_buffer;
}

// Export function to get buffer size
unsigned long __cosmos_get_debug_buffer_size(void) {
    return sizeof(cosmos_debug_buffer);
}
