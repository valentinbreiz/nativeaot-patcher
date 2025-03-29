#ifndef __LIB__IMAGE_H__
#define __LIB__IMAGE_H__

#include <limine_terminal/funcs.h>
#include <stdint.h>
#include <stddef.h>

struct image_t
{
    size_t x_size;
    size_t y_size;
    int type;
    uint8_t *img;
    int bpp;
    int pitch;
    size_t img_width; // x_size = scaled size, img_width = bitmap size
    size_t img_height;
    size_t x_displacement;
    size_t y_displacement;
    uint32_t back_colour;
};

enum image_style
{
    IMAGE_TILED,
    IMAGE_CENTERED,
    IMAGE_STRETCHED
};

#ifdef __cplusplus
extern "C" {
#endif

void image_make_centered(struct image_t *image, int frame_x_size, int frame_y_size, uint32_t back_colour);
void image_make_stretched(struct image_t *image, int new_x_size, int new_y_size);

struct image_t *image_open(void *buffer, size_t size);
void image_close(struct image_t *image);

#ifdef __cplusplus
}
#endif

#endif