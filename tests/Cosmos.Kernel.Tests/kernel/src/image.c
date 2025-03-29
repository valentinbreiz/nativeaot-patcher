#include <limine_terminal/stb_image.h>
#include <limine_terminal/image.h>
#include <stdint.h>
#include <stddef.h>

void image_make_centered(struct image_t *image, int frame_x_size, int frame_y_size, uint32_t back_colour)
{
    image->type = IMAGE_CENTERED;

    image->x_displacement = frame_x_size / 2 - image->x_size / 2;
    image->y_displacement = frame_y_size / 2 - image->y_size / 2;
    image->back_colour = back_colour;
}

void image_make_stretched(struct image_t *image, int new_x_size, int new_y_size)
{
    image->type = IMAGE_STRETCHED;

    image->x_size = new_x_size;
    image->y_size = new_y_size;
}

struct image_t *image_open(void *buffer, size_t size)
{
    struct image_t *image = term_alloc(sizeof(struct image_t));

    image->type = IMAGE_TILED;
    int x, y, bpp;

    image->img = stbi_load_from_memory(buffer, size, &x, &y, &bpp, 4);

    if (image->img == NULL)
    {
        term_free(image, sizeof(struct image_t));
        return NULL;
    }

    // Convert ABGR to XRGB
    uint32_t *pptr = (void *)image->img;
    for (int i = 0; i < x * y; i++)
        pptr[i] = (pptr[i] & 0x0000ff00) | ((pptr[i] & 0x00ff0000) >> 16) | ((pptr[i] & 0x000000ff) << 16);

    image->x_size = x;
    image->y_size = y;
    image->pitch = x * 4;
    image->bpp = 32;
    image->img_width = x;
    image->img_height = y;

    return image;
}

void image_close(struct image_t *image)
{
    stbi_image_free(image->img);
    term_free(image, sizeof(struct image_t));
}