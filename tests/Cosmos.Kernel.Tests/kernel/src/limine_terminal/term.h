#ifndef __LIB__GTERM_H__
#define __LIB__GTERM_H__

#include <limine_terminal/image.h>

#include <flanterm/backends/fb.h>
#include <flanterm/flanterm.h>

#include <stdbool.h>
#include <stdint.h>
#include <stddef.h>

#define TERM_FONT_SPACING 1
#define TERM_FONT_SCALE_X 1
#define TERM_FONT_SCALE_Y 1

#define TERM_ANSI_COLOURS { 0x00000000, 0x00AA0000, 0x0000AA00, 0x00AA5500, 0x000000AA, 0x00AA00AA, 0x0000AAAA, 0x00AAAAAA }
#define TERM_ANSI_BRIGHT_COLOURS { 0x00555555, 0x00FF5555, 0x0055FF55, 0x00FFFF55, 0x005555FF, 0x00FF55FF, 0x0055FFFF, 0x00FFFFFF }

#define TERM_BACKGROUND 0xA0000000
#define TERM_FOREGROUND 0x00AAAAAA

#define TERM_BACKGROUND_BRIGHT 0xA0555555
#define TERM_FOREGROUND_BRIGHT 0x00FFFFFF

#define TERM_MARGIN 64
#define TERM_MARGIN_GRADIENT 4

#define TERM_BACKDROP 0x00000000

#define TERM_MASK_SIZE 8
#define TERM_MASK_SHIFT_R 16
#define TERM_MASK_SHIFT_G 8
#define TERM_MASK_SHIFT_B 0

typedef struct flanterm_context term_t;

struct framebuffer_t
{
    uintptr_t address;
    uint64_t width;
    uint64_t height;
    uint64_t pitch;

    uint8_t red_mask_size;
    uint8_t red_mask_shift;
    uint8_t green_mask_size;
    uint8_t green_mask_shift;
    uint8_t blue_mask_size;
    uint8_t blue_mask_shift;
};

struct font_t
{
    uintptr_t address;
    uint8_t width;
    uint8_t height;
    uint8_t spacing;
    uint8_t scale_x;
    uint8_t scale_y;
};

struct style_t
{
    uint32_t ansi_colours[8];
    uint32_t ansi_bright_colours[8];
    uint32_t background;
    uint32_t foreground;
    uint32_t background_bright;
    uint32_t foreground_bright;
    uint16_t margin;
    uint16_t margin_gradient;
};

struct background_t
{
    struct image_t *background;
    enum image_style style;
    uint32_t backdrop;
};


#ifdef __cplusplus
extern "C" {
#endif

term_t *term_init(struct framebuffer_t frm, struct font_t font, struct style_t style, struct background_t backg);
void term_write(term_t *ctx, const char *buf, size_t count);

#ifdef __cplusplus
}
#endif

#endif