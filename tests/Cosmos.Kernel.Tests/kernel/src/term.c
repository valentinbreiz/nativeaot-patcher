#include <limine_terminal/funcs.h>
#include <limine_terminal/image.h>
#include <limine_terminal/term.h>

#define A(rgb) (uint8_t)(rgb >> 24)
#define R(rgb) (uint8_t)(rgb >> 16)
#define G(rgb) (uint8_t)(rgb >> 8)
#define B(rgb) (uint8_t)(rgb)
#define ARGB(a, r, g, b) (a << 24) | ((r & 0xFF) << 16) | ((g & 0xFF) << 8) | (b & 0xFF)

static uint64_t sqrt(uint64_t a_nInput)
{
    uint64_t op  = a_nInput;
    uint64_t res = 0;
    uint64_t one = (uint64_t)1 << 62;

    // "one" starts at the highest power of four <= than the argument.
    while (one > op)
        one >>= 2;

    while (one != 0)
    {
        if (op >= res + one)
        {
            op = op - (res + one);
            res = res +  2 * one;
        }
        res >>= 1;
        one >>= 2;
    }

    return res;
}

static inline uint32_t colour_blend(uint32_t fg, uint32_t bg)
{
    unsigned alpha = 255 - A(fg);
    unsigned inv_alpha = A(fg) + 1;

    uint8_t r = (uint8_t)((alpha * R(fg) + inv_alpha * R(bg)) / 256);
    uint8_t g = (uint8_t)((alpha * G(fg) + inv_alpha * G(bg)) / 256);
    uint8_t b = (uint8_t)((alpha * B(fg) + inv_alpha * B(bg)) / 256);

    return ARGB(0, r, g, b);
}


__attribute__((no_sanitize("undefined")))
static uint32_t blend_gradient_from_box(struct style_t style, struct framebuffer_t fb, size_t x, size_t y, uint32_t bg_px, uint32_t hex)
{
    size_t distance, x_distance, y_distance;
    size_t gradient_stop_x = fb.width - style.margin;
    size_t gradient_stop_y = fb.height - style.margin;

    if (x < style.margin)
        x_distance = style.margin - x;
    else
        x_distance = x - gradient_stop_x;

    if (y < style.margin)
        y_distance = style.margin - y;
    else
        y_distance = y - gradient_stop_y;

    if (x >= style.margin && x < gradient_stop_x)
        distance = y_distance;
    else if (y >= style.margin && y < gradient_stop_y)
        distance = x_distance;
    else
        distance = sqrt((uint64_t)x_distance * (uint64_t)x_distance
            + (uint64_t)y_distance * (uint64_t)y_distance);

    if (distance > style.margin_gradient)
        return bg_px;

    uint8_t gradient_step = (0xff - A(hex)) / style.margin_gradient;
    uint8_t new_alpha = A(hex) + gradient_step * distance;

    return colour_blend((hex & 0xffffff) | (new_alpha << 24), bg_px);
}

typedef size_t fixedp6; // the last 6 bits are the fixed point part
static size_t fixedp6_to_int(fixedp6 value)
{
    return value / 64;
}
static fixedp6 int_to_fixedp6(size_t value)
{
    return value * 64;
}

// Draw rect at coordinates, copying from the image to the fb and canvas, applying fn on every pixel
__attribute__((always_inline, no_sanitize("alignment")))
static inline void genloop(struct style_t style, struct image_t *background, uint32_t *bg_canvas, struct framebuffer_t fb, size_t xstart, size_t xend, size_t ystart, size_t yend, uint32_t (*blend)(struct style_t style, struct framebuffer_t fb, size_t x, size_t y, uint32_t orig))
{
    uint8_t *img = background->img;
    const size_t img_width = background->img_width, img_height = background->img_height, img_pitch = background->pitch, colsize = background->bpp / 8;

    switch (background->type)
    {
        case IMAGE_TILED:
            for (size_t y = ystart; y < yend; y++)
            {
                size_t image_y = y % img_height, image_x = xstart % img_width;
                const size_t off = img_pitch * image_y;
                size_t canvas_off = fb.width * y;
                for (size_t x = xstart; x < xend; x++)
                {
                    uint32_t img_pixel = *(uint32_t *)(img + image_x * colsize + off);
                    uint32_t i = blend(style, fb, x, y, img_pixel);
                    bg_canvas[canvas_off + x] = i;
                    if (image_x++ == img_width)
                        image_x = 0; // image_x = x % img_width, but modulo is too expensive
                }
            }
            break;

        case IMAGE_CENTERED:
            for (size_t y = ystart; y < yend; y++)
            {
                size_t image_y = y - background->y_displacement;
                const size_t off = img_pitch * image_y;
                size_t canvas_off = fb.width * y;
                if (image_y >= background->y_size)
                { /* external part */
                    for (size_t x = xstart; x < xend; x++)
                    {
                        uint32_t i = blend(style, fb, x, y, background->back_colour);
                        bg_canvas[canvas_off + x] = i;
                    }
                }
                else
                { /* internal part */
                    for (size_t x = xstart; x < xend; x++)
                    {
                        size_t image_x = (x - background->x_displacement);
                        bool x_external = image_x >= background->x_size;
                        uint32_t img_pixel = *(uint32_t *)(img + image_x * colsize + off);
                        uint32_t i = blend(style, fb, x, y, x_external ? background->back_colour : img_pixel);
                        bg_canvas[canvas_off + x] = i;
                    }
                }
            }
            break;
        // For every pixel, ratio = img_width / gterm_width, img_x = x * ratio, x = (xstart + i)
        // hence x = xstart * ratio + i * ratio
        // so you can set x = xstart * ratio, and increment by ratio at each iteration
        case IMAGE_STRETCHED:
            for (size_t y = ystart; y < yend; y++)
            {
                size_t img_y = (y * img_height) / fb.height; // calculate Y with full precision
                size_t off = img_pitch * img_y;
                size_t canvas_off = fb.width * y;

                size_t ratio = int_to_fixedp6(img_width) / fb.width;
                fixedp6 img_x = ratio * xstart;
                for (size_t x = xstart; x < xend; x++)
                {
                    uint32_t img_pixel = *(uint32_t *)(img + fixedp6_to_int(img_x) * colsize + off);
                    uint32_t i = blend(style, fb, x, y, img_pixel);
                    bg_canvas[canvas_off + x] = i;
                    img_x += ratio;
                }
            }
            break;
    }
}

static uint32_t blend_external(struct style_t style, struct framebuffer_t fb, size_t x, size_t y, uint32_t orig)
{
    (void)style;
    (void)fb;
    (void)x;
    (void)y;
    return orig;
}
static uint32_t blend_internal(struct style_t style, struct framebuffer_t fb, size_t x, size_t y, uint32_t orig)
{
    (void)fb;
    (void)x;
    (void)y;
    return colour_blend(style.background, orig);
}
static uint32_t blend_margin(struct style_t style, struct framebuffer_t fb, size_t x, size_t y, uint32_t orig)
{
    return blend_gradient_from_box(style, fb, x, y, orig, style.background);
}

static void loop_external(struct style_t style, struct image_t *background, uint32_t *bg_canvas, struct framebuffer_t fb, size_t xstart, size_t xend, size_t ystart, size_t yend)
{
    genloop(style, background, bg_canvas, fb, xstart, xend, ystart, yend, blend_external);
}
static void loop_margin(struct style_t style, struct image_t *background, uint32_t *bg_canvas, struct framebuffer_t fb, size_t xstart, size_t xend, size_t ystart, size_t yend)
{
    genloop(style, background, bg_canvas, fb, xstart, xend, ystart, yend, blend_margin);
}
static void loop_internal(struct style_t style, struct image_t *background, uint32_t *bg_canvas, struct framebuffer_t fb, size_t xstart, size_t xend, size_t ystart, size_t yend)
{
    genloop(style, background, bg_canvas, fb, xstart, xend, ystart, yend, blend_internal);
}

static void generate_canvas(struct style_t style, struct image_t *background, size_t *bg_canvas_size, uint32_t **bg_canvas, struct framebuffer_t fb)
{
    if (background != NULL)
    {
        *bg_canvas_size = fb.width * fb.height * sizeof(uint32_t);
        *bg_canvas = term_alloc(*bg_canvas_size);

        int64_t margin_no_gradient = (int64_t)style.margin - style.margin_gradient;

        if (margin_no_gradient < 0)
            margin_no_gradient = 0;

        size_t scan_stop_x = fb.width - margin_no_gradient;
        size_t scan_stop_y = fb.height - margin_no_gradient;

#define SAME_ARGS style, background, *bg_canvas, fb
        loop_external(SAME_ARGS, 0, fb.width, 0, margin_no_gradient);
        loop_external(SAME_ARGS, 0, fb.width, scan_stop_y, fb.height);
        loop_external(SAME_ARGS, 0, margin_no_gradient, margin_no_gradient, scan_stop_y);
        loop_external(SAME_ARGS, scan_stop_x, fb.width, margin_no_gradient, scan_stop_y);

        size_t gradient_stop_x = fb.width - style.margin;
        size_t gradient_stop_y = fb.height - style.margin;

        if (style.margin_gradient)
        {
            loop_margin(SAME_ARGS, margin_no_gradient, scan_stop_x, margin_no_gradient, style.margin);
            loop_margin(SAME_ARGS, margin_no_gradient, scan_stop_x, gradient_stop_y, scan_stop_y);
            loop_margin(SAME_ARGS, margin_no_gradient, style.margin, style.margin, gradient_stop_y);
            loop_margin(SAME_ARGS, gradient_stop_x, scan_stop_x, style.margin, gradient_stop_y);
        }

        loop_internal(SAME_ARGS, style.margin, gradient_stop_x, style.margin, gradient_stop_y);
#undef SAME_ARGS
    }
    else *bg_canvas = NULL;
}

term_t *term_init(struct framebuffer_t frm, struct font_t font, struct style_t style, struct background_t backg)
{
    if (backg.background == NULL)
    {
        style.margin = 0;
        style.margin_gradient = 0;
    }

    if (font.scale_x < 1)
        font.scale_x = 1;
    if (font.scale_y < 1)
        font.scale_y = 1;

#define FONT_MAX 16384
    size_t font_size = (font.width * font.height * FLANTERM_FB_FONT_GLYPHS) / 8;
    if (font_size > FONT_MAX)
        goto no_load_font;

no_load_font:;

    if (backg.background != NULL)
    {
        switch (backg.style)
        {
            case IMAGE_CENTERED:
                image_make_centered(backg.background, frm.width, frm.height, backg.backdrop);
                break;
            case IMAGE_TILED:
                break;
            case IMAGE_STRETCHED:
                image_make_stretched(backg.background, frm.width, frm.height);
                break;
        }
    }

    size_t bg_canvas_size;
    uint32_t *bg_canvas;
    generate_canvas(style, backg.background, &bg_canvas_size, &bg_canvas, frm);

    term_t *term = flanterm_fb_init(
        term_alloc, term_free,
        (uint32_t *)frm.address, frm.width, frm.height, frm.pitch,
        frm.red_mask_size, frm.red_mask_shift,
        frm.green_mask_size, frm.green_mask_shift,
        frm.blue_mask_size, frm.blue_mask_shift,
        bg_canvas,
        style.ansi_colours, style.ansi_bright_colours,
        &style.background, &style.foreground,
        &style.background_bright, &style.foreground_bright,
        (void *)font.address, font.width, font.height, font.spacing,
        font.scale_x, font.scale_y,
        style.margin
    );

    if (bg_canvas != NULL)
        term_free(bg_canvas, bg_canvas_size);

    if (backg.background != NULL)
    {
        image_close(backg.background);
        backg.background = NULL;
    }

    flanterm_context_reinit(term);
    return term;
}

void term_write(term_t *ctx, const char *buf, size_t count)
{
    flanterm_write(ctx, buf, count);
}