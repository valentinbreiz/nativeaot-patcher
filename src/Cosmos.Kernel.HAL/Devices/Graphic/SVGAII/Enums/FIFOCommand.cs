using System;

namespace Cosmos.Kernel.HAL.Devices.Graphic.SVGAII;

public enum FIFOCommand
{
    /// <summary>
    /// Update.
    /// </summary>
    Update = 1,
    /// <summary>
    /// Rectange fill.
    /// </summary>
    RECT_FILL = 2,
    /// <summary>
    /// Rectange copy.
    /// </summary>
    RECT_COPY = 3,
    /// <summary>
    /// Define bitmap.
    /// </summary>
    DEFINE_BITMAP = 4,
    /// <summary>
    /// Define bitmap scanline.
    /// </summary>
    DEFINE_BITMAP_SCANLINE = 5,
    /// <summary>
    /// Define pixmap.
    /// </summary>
    DEFINE_PIXMAP = 6,
    /// <summary>
    /// Define pixmap scanline.
    /// </summary>
    DEFINE_PIXMAP_SCANLINE = 7,
    /// <summary>
    /// Rectange bitmap fill.
    /// </summary>
    RECT_BITMAP_FILL = 8,
    /// <summary>
    /// Rectange pixmap fill.
    /// </summary>
    RECT_PIXMAP_FILL = 9,
    /// <summary>
    /// Rectange bitmap copy.
    /// </summary>
    RECT_BITMAP_COPY = 10,
    /// <summary>
    /// Rectange pixmap fill.
    /// </summary>
    RECT_PIXMAP_COPY = 11,
    /// <summary>
    /// Free object.
    /// </summary>
    FREE_OBJECT = 12,
    /// <summary>
    /// Rectangle raster operation fill.
    /// </summary>
    RECT_ROP_FILL = 13,
    /// <summary>
    /// Rectangle raster operation copy.
    /// </summary>
    RECT_ROP_COPY = 14,
    /// <summary>
    /// Rectangle raster operation bitmap fill.
    /// </summary>
    RECT_ROP_BITMAP_FILL = 15,
    /// <summary>
    /// Rectangle raster operation pixmap fill.
    /// </summary>
    RECT_ROP_PIXMAP_FILL = 16,
    /// <summary>
    /// Rectangle raster operation bitmap copy.
    /// </summary>
    RECT_ROP_BITMAP_COPY = 17,
    /// <summary>
    /// Rectangle raster operation pixmap copy.
    /// </summary>
    RECT_ROP_PIXMAP_COPY = 18,
    /// <summary>
    /// Define cursor.
    /// </summary>
    DEFINE_CURSOR = 19,
    /// <summary>
    /// Display cursor.
    /// </summary>
    DISPLAY_CURSOR = 20,
    /// <summary>
    /// Move cursor.
    /// </summary>
    MOVE_CURSOR = 21,
    /// <summary>
    /// Define alpha cursor.
    /// </summary>
    DEFINE_ALPHA_CURSOR = 22,

    DEFINE_SURFACE = 1040,
    SURFACE_COPY = 1040 + 2,
    SETMATERIAL = 1040 + 12,
    SETLIGHTDATA = 1040 + 13,
    SETLIGHTENABLE = 1040 + 14,
    SETVIEWPORT = 1040 + 15,
    SETZRANGE = 1040 + 8,

    DEFINE_CONTEXT = 1040 + 5,
    DESTROY_CONTEXT = 1040 + 6,
    DEFINE_SURFACE_V2 = 1040 + 30,  // Use V2 surface definition
    DESTROY_SURFACE = 1040 + 1,
    DESTROY_SHADER = 1040 + 20,
    SET_RENDER_TARGET = 1040 + 10,
    CLEAR = 1040 + 17,
    SET_VIEWPORT = 1040 + 15,
    SET_ZRANGE = 1040 + 8,
    PRESENT = 1040 + 18,
    SETRENDERSTATE = 1040 + 9,
    SURFACE_DMA = 1040 + 4,
    DRAW_PRIMITIVES = 1040 + 23,
    SETTRANSFORM = 1040 + 7,
    SETTEXTURESTATE = 1040 + 11,
    SHADER_DEFINE = 1040 + 19,
    SET_SHADER = 1040 + 21,
    SET_SHADER_CONST = 1040 + 22,

    // SVGAII 2.3 commands
    SVGA_3D_CMD_DEFINE_GB_MOB = 1093,
	SVGA_3D_CMD_DESTROY_GB_MOB = 1094,
	SVGA_3D_CMD_DEAD3 = 1095,
	SVGA_3D_CMD_UPDATE_GB_MOB_MAPPING = 1096,

	SVGA_3D_CMD_DEFINE_GB_SURFACE = 1097,
	SVGA_3D_CMD_DESTROY_GB_SURFACE = 1098,
	SVGA_3D_CMD_BIND_GB_SURFACE = 1099,
	SVGA_3D_CMD_COND_BIND_GB_SURFACE = 1100,
	SVGA_3D_CMD_UPDATE_GB_IMAGE = 1101,
	SVGA_3D_CMD_UPDATE_GB_SURFACE = 1102,
	SVGA_3D_CMD_READBACK_GB_IMAGE = 1103,
	SVGA_3D_CMD_READBACK_GB_SURFACE = 1104,
	SVGA_3D_CMD_INVALIDATE_GB_IMAGE = 1105,
	SVGA_3D_CMD_INVALIDATE_GB_SURFACE = 1106,

	SVGA_3D_CMD_DEFINE_GB_CONTEXT = 1107,
	SVGA_3D_CMD_DESTROY_GB_CONTEXT = 1108,
	SVGA_3D_CMD_BIND_GB_CONTEXT = 1109,
	SVGA_3D_CMD_READBACK_GB_CONTEXT = 1110,
	SVGA_3D_CMD_INVALIDATE_GB_CONTEXT = 1111,

	SVGA_3D_CMD_DEFINE_GB_SHADER = 1112,
	SVGA_3D_CMD_DESTROY_GB_SHADER = 1113,
	SVGA_3D_CMD_BIND_GB_SHADER = 1114,

	SVGA_3D_CMD_SET_OTABLE_BASE64 = 1115,

	SVGA_3D_CMD_BEGIN_GB_QUERY = 1116,
	SVGA_3D_CMD_END_GB_QUERY = 1117,
	SVGA_3D_CMD_WAIT_FOR_GB_QUERY = 1118,
    SVGA_3D_CMD_DEFINE_GB_SCREENTARGET = 1124,
	SVGA_3D_CMD_DESTROY_GB_SCREENTARGET = 1125,
	SVGA_3D_CMD_BIND_GB_SCREENTARGET = 1126,
	SVGA_3D_CMD_UPDATE_GB_SCREENTARGET = 1127,

	SVGA_3D_CMD_READBACK_GB_IMAGE_PARTIAL = 1128,
	SVGA_3D_CMD_INVALIDATE_GB_IMAGE_PARTIAL = 1129,

	SVGA_3D_CMD_SET_GB_SHADERCONSTS_INLINE = 1130,

	SVGA_3D_CMD_GB_SCREEN_DMA = 1131,
	SVGA_3D_CMD_BIND_GB_SURFACE_WITH_PITCH = 1132,
	SVGA_3D_CMD_GB_MOB_FENCE = 1133,
	SVGA_3D_CMD_DEFINE_GB_SURFACE_V2 = 1134,
	SVGA_3D_CMD_DEFINE_GB_MOB64 = 1135,
	SVGA_3D_CMD_REDEFINE_GB_MOB64 = 1136,
    SVGA_3D_CMD_UPDATE_GB_SCREENTARGET_V2 = 1266,

	SVGA_3D_CMD_DEFINE_GB_SURFACE_V4 = 1267,
}
