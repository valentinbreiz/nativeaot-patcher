#include <stdint.h>
#include <stddef.h>
#include <stdbool.h>
#include <limine.h>
#include <limine_terminal/term.h>

/* ------------------------
   Simple bump allocator
   ------------------------ */
#define MEMPOOL_SIZE (1024 * 1024)
static uint8_t mempool[MEMPOOL_SIZE];
static size_t mempool_offset = 0;

void *term_alloc(size_t size) {
    if (mempool_offset + size > MEMPOOL_SIZE)
        return NULL;
    void *ptr = &mempool[mempool_offset];
    mempool_offset += size;
    return ptr;
}

void *term_realloc(void *oldptr, size_t size) {
    void *newptr = term_alloc(size);
    if (!newptr)
        return NULL;
    term_memcpy(newptr, oldptr, size);
    return newptr;
}

void term_free(void *ptr, size_t size) {
    (void)ptr;
    (void)size;
}

void term_freensz(void *ptr) {
    (void)ptr;
}

void *term_memcpy(void *dest, const void *src, size_t size) {
    uint8_t *d = (uint8_t *)dest;
    const uint8_t *s = (const uint8_t *)src;
    for (size_t i = 0; i < size; i++) {
        d[i] = s[i];
    }
    return dest;
}

void *term_memset(void *dest, int val, size_t count) {
    uint8_t *d = (uint8_t *)dest;
    for (size_t i = 0; i < count; i++) {
        d[i] = (uint8_t)val;
    }
    return dest;
}

// Implémentation de memset
void *memset(void *dest, int val, size_t count) {
    uint8_t *d = (uint8_t *)dest;
    for (size_t i = 0; i < count; i++) {
        d[i] = (uint8_t)val;
    }
    return dest;
}

// Implémentation de memcpy
void *memcpy(void *dest, const void *src, size_t count) {
    uint8_t *d = (uint8_t *)dest;
    const uint8_t *s = (const uint8_t *)src;
    for (size_t i = 0; i < count; i++) {
        d[i] = s[i];
    }
    return dest;
}

/* ------------------------
   Configuration de la console et du background
   ------------------------ */
struct image_t *image = NULL; // Désactivez le fond pour l'instant

struct framebuffer_t frm = {
    .address = 0,  // Remplir dynamiquement dans kmain
    .width = 0,
    .height = 0,
    .pitch = 0,
    .red_mask_size = 0,
    .red_mask_shift = 0,
    .green_mask_size = 0,
    .green_mask_shift = 0,
    .blue_mask_size = 0,
    .blue_mask_shift = 0
};

struct font_t font = {
    .address = 0, // Pas de police bitmap pour l'instant
    .width = 8,
    .height = 16,
    .spacing = TERM_FONT_SPACING,
    .scale_x = TERM_FONT_SCALE_X,
    .scale_y = TERM_FONT_SCALE_Y,
};

struct style_t style = {
    .ansi_colours = TERM_ANSI_COLOURS,
    .ansi_bright_colours = TERM_ANSI_BRIGHT_COLOURS,
    .background = TERM_BACKGROUND,
    .foreground = TERM_FOREGROUND_BRIGHT,
    .background_bright = TERM_BACKGROUND_BRIGHT,
    .foreground_bright = TERM_FOREGROUND_BRIGHT,
    .margin = TERM_MARGIN,
    .margin_gradient = TERM_MARGIN_GRADIENT
};

struct background_t back = {
    .background = NULL, // Désactivez le fond
    .backdrop = TERM_BACKDROP
};

/* ------------------------
   Requête de terminal Limine
   ------------------------ */
__attribute__((used, section(".limine_requests")))
static volatile struct limine_terminal_request terminal_request = {
    .id = LIMINE_TERMINAL_REQUEST,
    .revision = 1 // Utilisez la révision 1 pour éviter les avertissements
};

/* ------------------------
   Fonction d'arrêt (boucle infinie)
   ------------------------ */
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
   Point d'entrée du kernel
   ------------------------ */
void kmain(void) {
    // Initialisation du terminal
    term_t *term = term_init(frm, font, style, back);
    if (!term) {
        hcf();
    }

    // Exemple d'écriture sur la console
    term_write(term, "Hello, World!", 13);

    // Boucle infinie
    hcf();
}
