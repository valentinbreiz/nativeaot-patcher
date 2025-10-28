using Cosmos.Kernel.Core.Memory;
using Cosmos.Kernel.Core.Runtime;
using Cosmos.Kernel.Core.IO;

namespace Cosmos.Kernel.Graphics.Fonts;

public static unsafe class PCScreenFont
{
    public static class Default
    {
        public const string DefaultFontKey = "Cosmos.Kernel.Graphics.Fonts.DefaultFont";
        public const string DefaultFontName = $"{DefaultFontKey}.psf";
    }
    public static byte* Framebuffer;
    public static int Scanline;
    public static byte* FontData;
    public static ushort* UnicodeTable = null; // Optional
    private static bool Initialized;
    public static ushort Magic = 0x0436;
    public static uint PSFFontMagic = 0x864ab572;

    private static void EnsureInitialized()
    {
        if (!Initialized)
        {
            var embeddedResourceName = AppContext.GetData(Default.DefaultFontKey)?.ToString() ?? Default.DefaultFontName;
            var resourceSpan = ResourceManager.GetResourceAsSpan(embeddedResourceName);
            Serial.WriteString("Loading default PSF font from resources...\n");
            Serial.WriteString($"Font Key: {embeddedResourceName}");
            Serial.WriteString($"Font size: {resourceSpan.Length.ToString()} bytes\n");

            fixed (byte* ptr = resourceSpan)
            {
                Init((byte*)Graphics.Canvas.Address, (int)Graphics.Canvas.Pitch, ptr);
            }
            //byte* fontData = Base64.Decode(Default.GetUnmanagedFontData(), (uint)Default.Size);
            //Init((byte*)Graphics.Canvas.Address, (int)Graphics.Canvas.Pitch, fontData);
            Initialized = true;
        }
    }

    public static int CharWidth
    {
        get
        {
            EnsureInitialized();
            return (int)((PSF_Font*)FontData)->Width;
        }
    }

    public static int CharHeight
    {
        get
        {
            EnsureInitialized();
            return (int)((PSF_Font*)FontData)->Height;
        }
    }

    public struct PSF_Header
    {
        public ushort Magic;
        public byte Mode;
        public byte CharSize;
    }

    public struct PSF_Font
    {
        public uint Magic; // PSF magic number
        public uint Version; // Always 0
        public uint HeaderSize; // Offset of the bitmaps in the file
        public uint Flags; // 0 if no unicode table
        public uint NumGlyph; // Number of glyphs in the font
        public uint BytesPerGlyph; // size of each glyph
        public uint Height; // Height in pixels
        public uint Width; // Width in pixels
    }

    // You can initialize things here
    public static void Init(byte* fb, int scanline, byte* fontData)
    {
        Framebuffer = fb;
        Scanline = scanline;
        FontData = fontData;
    }

    // Example allocator use
    public static void LoadFont(byte* fontFile, uint Length)
    {
        FontData = (byte*)MemoryOp.Alloc(Length);
        for (int i = 0; i < Length; i++)
            FontData[i] = fontFile[i];
    }

    public static void PutString(string str, int x, int y, uint fg, uint bg)
    {
        int charWidth = CharWidth;
        fixed (char* ptr = str)
        {
            for (int i = 0; i < str.Length; i++)
            {
                PutChar(ptr[i], x + i * charWidth, y, fg, bg);
            }
        }
    }

    public static void PutString(char* str, int x, int y, uint fg, uint bg)
    {
        int charWidth = CharWidth;
        for (int i = 0; str[i] != 0; i++)
        {
            PutChar(str[i], x + i * charWidth, y, fg, bg);
        }
    }

    public static void PutChar(
        ushort c, int cx, int cy,
        uint fg, uint bg)
    {

        EnsureInitialized();

        PSF_Font* font = (PSF_Font*)FontData;
        int bytesPerLine = ((int)font->Width + 7) / 8;

        // Ensure the character is within the ASCII range (0-127)
        c = (ushort)(c & 0x7F);

        byte* glyph = FontData
            + font->HeaderSize
            + (c < font->NumGlyph ? c : 0) * font->BytesPerGlyph;

        int offs = (int)(cy * font->Height * Scanline +
                   cx * (int)font->Width * sizeof(uint));

        for (int y = 0; y < font->Height; y++)
        {
            int line = offs;

            int rowData = 0;
            for (int b = 0; b < bytesPerLine; b++)
            {
                rowData |= glyph[b] << (8 * (bytesPerLine - 1 - b));
            }

            int mask = 1 << ((int)font->Width - 1);

            for (int x = 0; x < font->Width; x++)
            {


                if ((rowData & mask) != 0)
                {
                    Graphics.Canvas.DrawPixel(fg, cx + x, cy + y);
                }
                else if ((bg & 0xFF000000) != 0)
                {
                    Graphics.Canvas.DrawPixel(bg, cx + x, cy + y);
                }

                mask >>= 1;
            }

            glyph += bytesPerLine;
            offs += Scanline;
        }
    }
}

