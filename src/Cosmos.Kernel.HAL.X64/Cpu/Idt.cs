// This code is licensed under MIT license (see LICENSE for details)

using System.Runtime.InteropServices;
using Cosmos.Kernel.Core.Memory.Heap;

namespace Cosmos.Kernel.HAL.X64.Cpu;

/// <summary>
/// Handles Interrupt Descriptor Table initialization for x86_64.
/// </summary>
public static unsafe partial class Idt
{
    [LibraryImport("*", EntryPoint = "__load_lidt")]
    private static partial void LoadIdt(IdtPointer* ptr);

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct IdtPointer
    {
        public ushort Limit;
        public ulong Base;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct IdtEntry
    {
        public ushort OffsetLow;
        public ushort Selector;
        public byte Ist;
        public byte TypeAttr;
        public ushort OffsetMid;
        public uint OffsetHigh;
        public uint Reserved;

        public void SetOffset(void* addr)
        {
            ulong address = (ulong)addr;
            OffsetLow = (ushort)(address & 0xFFFF);
            OffsetMid = (ushort)((address >> 16) & 0xFFFF);
            OffsetHigh = (uint)((address >> 32) & 0xFFFFFFFF);
        }
    }

    private static IdtEntry[] IdtEntries = new IdtEntry[256];

    /// <summary>
    /// Registers all IRQ stubs in the IDT.
    /// </summary>
    public static void RegisterAllInterrupts()
    {

        for (int i = 0; i < 256; i++)
        {
            IdtEntries[i].Selector = 0x08; // Kernel code segment
            IdtEntries[i].TypeAttr = 0x8E; // Interrupt gate, present
            IdtEntries[i].Ist = 0; // No IST
            IdtEntries[i].Reserved = 0;

            IdtEntries[i].SetOffset(GetStubAddress(i));
        }

        // Load the IDT
        fixed (void* ptr = &IdtEntries[0])
        {
            IdtPointer idtPtr;
            idtPtr.Base = (ulong)ptr;
            idtPtr.Limit = (ushort)(sizeof(IdtEntry) * 256 - 1);
            LoadIdt(&idtPtr);
        }
    }

    /// <summary>
    /// Returns the pointer to the corresponding IRQ stub.
    /// Must match the YASM-defined global labels.
    /// </summary>
    /// <param name="irq">IRQ number 0–255</param>
    /// <returns>Pointer to stub</returns>
    private static void* GetStubAddress(int irq) =>
        irq switch
        {
            0 => irq0_stub(),
            1 => irq1_stub(),
            2 => irq2_stub(),
            3 => irq3_stub(),
            4 => irq4_stub(),
            5 => irq5_stub(),
            6 => irq6_stub(),
            7 => irq7_stub(),
            8 => irq8_stub(),
            9 => irq9_stub(),
            10 => irq10_stub(),
            11 => irq11_stub(),
            12 => irq12_stub(),
            13 => irq13_stub(),
            14 => irq14_stub(),
            15 => irq15_stub(),
            16 => irq16_stub(),
            17 => irq17_stub(),
            18 => irq18_stub(),
            19 => irq19_stub(),
            20 => irq20_stub(),
            21 => irq21_stub(),
            22 => irq22_stub(),
            23 => irq23_stub(),
            24 => irq24_stub(),
            25 => irq25_stub(),
            26 => irq26_stub(),
            27 => irq27_stub(),
            28 => irq28_stub(),
            29 => irq29_stub(),
            30 => irq30_stub(),
            31 => irq31_stub(),
            32 => irq32_stub(),
            33 => irq33_stub(),
            34 => irq34_stub(),
            35 => irq35_stub(),
            36 => irq36_stub(),
            37 => irq37_stub(),
            38 => irq38_stub(),
            39 => irq39_stub(),
            40 => irq40_stub(),
            41 => irq41_stub(),
            42 => irq42_stub(),
            43 => irq43_stub(),
            44 => irq44_stub(),
            45 => irq45_stub(),
            46 => irq46_stub(),
            47 => irq47_stub(),
            48 => irq48_stub(),
            49 => irq49_stub(),
            50 => irq50_stub(),
            51 => irq51_stub(),
            52 => irq52_stub(),
            53 => irq53_stub(),
            54 => irq54_stub(),
            55 => irq55_stub(),
            56 => irq56_stub(),
            57 => irq57_stub(),
            58 => irq58_stub(),
            59 => irq59_stub(),
            60 => irq60_stub(),
            61 => irq61_stub(),
            62 => irq62_stub(),
            63 => irq63_stub(),
            64 => irq64_stub(),
            65 => irq65_stub(),
            66 => irq66_stub(),
            67 => irq67_stub(),
            68 => irq68_stub(),
            69 => irq69_stub(),
            70 => irq70_stub(),
            71 => irq71_stub(),
            72 => irq72_stub(),
            73 => irq73_stub(),
            74 => irq74_stub(),
            75 => irq75_stub(),
            76 => irq76_stub(),
            77 => irq77_stub(),
            78 => irq78_stub(),
            79 => irq79_stub(),
            80 => irq80_stub(),
            81 => irq81_stub(),
            82 => irq82_stub(),
            83 => irq83_stub(),
            84 => irq84_stub(),
            85 => irq85_stub(),
            86 => irq86_stub(),
            87 => irq87_stub(),
            88 => irq88_stub(),
            89 => irq89_stub(),
            90 => irq90_stub(),
            91 => irq91_stub(),
            92 => irq92_stub(),
            93 => irq93_stub(),
            94 => irq94_stub(),
            95 => irq95_stub(),
            96 => irq96_stub(),
            97 => irq97_stub(),
            98 => irq98_stub(),
            99 => irq99_stub(),
            100 => irq100_stub(),
            101 => irq101_stub(),
            102 => irq102_stub(),
            103 => irq103_stub(),
            104 => irq104_stub(),
            105 => irq105_stub(),
            106 => irq106_stub(),
            107 => irq107_stub(),
            108 => irq108_stub(),
            109 => irq109_stub(),
            110 => irq110_stub(),
            111 => irq111_stub(),
            112 => irq112_stub(),
            113 => irq113_stub(),
            114 => irq114_stub(),
            115 => irq115_stub(),
            116 => irq116_stub(),
            117 => irq117_stub(),
            118 => irq118_stub(),
            119 => irq119_stub(),
            120 => irq120_stub(),
            121 => irq121_stub(),
            122 => irq122_stub(),
            123 => irq123_stub(),
            124 => irq124_stub(),
            125 => irq125_stub(),
            126 => irq126_stub(),
            127 => irq127_stub(),
            128 => irq128_stub(),
            129 => irq129_stub(),
            130 => irq130_stub(),
            131 => irq131_stub(),
            132 => irq132_stub(),
            133 => irq133_stub(),
            134 => irq134_stub(),
            135 => irq135_stub(),
            136 => irq136_stub(),
            137 => irq137_stub(),
            138 => irq138_stub(),
            139 => irq139_stub(),
            140 => irq140_stub(),
            141 => irq141_stub(),
            142 => irq142_stub(),
            143 => irq143_stub(),
            144 => irq144_stub(),
            145 => irq145_stub(),
            146 => irq146_stub(),
            147 => irq147_stub(),
            148 => irq148_stub(),
            149 => irq149_stub(),
            150 => irq150_stub(),
            151 => irq151_stub(),
            152 => irq152_stub(),
            153 => irq153_stub(),
            154 => irq154_stub(),
            155 => irq155_stub(),
            156 => irq156_stub(),
            157 => irq157_stub(),
            158 => irq158_stub(),
            159 => irq159_stub(),
            160 => irq160_stub(),
            161 => irq161_stub(),
            162 => irq162_stub(),
            163 => irq163_stub(),
            164 => irq164_stub(),
            165 => irq165_stub(),
            166 => irq166_stub(),
            167 => irq167_stub(),
            168 => irq168_stub(),
            169 => irq169_stub(),
            170 => irq170_stub(),
            171 => irq171_stub(),
            172 => irq172_stub(),
            173 => irq173_stub(),
            174 => irq174_stub(),
            175 => irq175_stub(),
            176 => irq176_stub(),
            177 => irq177_stub(),
            178 => irq178_stub(),
            179 => irq179_stub(),
            180 => irq180_stub(),
            181 => irq181_stub(),
            182 => irq182_stub(),
            183 => irq183_stub(),
            184 => irq184_stub(),
            185 => irq185_stub(),
            186 => irq186_stub(),
            187 => irq187_stub(),
            188 => irq188_stub(),
            189 => irq189_stub(),
            190 => irq190_stub(),
            191 => irq191_stub(),
            192 => irq192_stub(),
            193 => irq193_stub(),
            194 => irq194_stub(),
            195 => irq195_stub(),
            196 => irq196_stub(),
            197 => irq197_stub(),
            198 => irq198_stub(),
            199 => irq199_stub(),
            200 => irq200_stub(),
            201 => irq201_stub(),
            202 => irq202_stub(),
            203 => irq203_stub(),
            204 => irq204_stub(),
            205 => irq205_stub(),
            206 => irq206_stub(),
            207 => irq207_stub(),
            208 => irq208_stub(),
            209 => irq209_stub(),
            210 => irq210_stub(),
            211 => irq211_stub(),
            212 => irq212_stub(),
            213 => irq213_stub(),
            214 => irq214_stub(),
            215 => irq215_stub(),
            216 => irq216_stub(),
            217 => irq217_stub(),
            218 => irq218_stub(),
            219 => irq219_stub(),
            220 => irq220_stub(),
            221 => irq221_stub(),
            222 => irq222_stub(),
            223 => irq223_stub(),
            224 => irq224_stub(),
            225 => irq225_stub(),
            226 => irq226_stub(),
            227 => irq227_stub(),
            228 => irq228_stub(),
            229 => irq229_stub(),
            230 => irq230_stub(),
            231 => irq231_stub(),
            232 => irq232_stub(),
            233 => irq233_stub(),
            234 => irq234_stub(),
            235 => irq235_stub(),
            236 => irq236_stub(),
            237 => irq237_stub(),
            238 => irq238_stub(),
            239 => irq239_stub(),
            240 => irq240_stub(),
            241 => irq241_stub(),
            242 => irq242_stub(),
            243 => irq243_stub(),
            244 => irq244_stub(),
            245 => irq245_stub(),
            246 => irq246_stub(),
            247 => irq247_stub(),
            248 => irq248_stub(),
            249 => irq249_stub(),
            250 => irq250_stub(),
            251 => irq251_stub(),
            252 => irq252_stub(),
            253 => irq253_stub(),
            254 => irq254_stub(),
            255 => irq255_stub(),
            _ => null
        };

    [LibraryImport("*", EntryPoint = "irq0_stub")]
    private static partial void* irq0_stub();

    [LibraryImport("*", EntryPoint = "irq1_stub")]
    private static partial void* irq1_stub();

    [LibraryImport("*", EntryPoint = "irq2_stub")]
    private static partial void* irq2_stub();

    [LibraryImport("*", EntryPoint = "irq3_stub")]
    private static partial void* irq3_stub();

    [LibraryImport("*", EntryPoint = "irq4_stub")]
    private static partial void* irq4_stub();

    [LibraryImport("*", EntryPoint = "irq5_stub")]
    private static partial void* irq5_stub();

    [LibraryImport("*", EntryPoint = "irq6_stub")]
    private static partial void* irq6_stub();

    [LibraryImport("*", EntryPoint = "irq7_stub")]
    private static partial void* irq7_stub();

    [LibraryImport("*", EntryPoint = "irq8_stub")]
    private static partial void* irq8_stub();

    [LibraryImport("*", EntryPoint = "irq9_stub")]
    private static partial void* irq9_stub();

    [LibraryImport("*", EntryPoint = "irq10_stub")]
    private static partial void* irq10_stub();

    [LibraryImport("*", EntryPoint = "irq11_stub")]
    private static partial void* irq11_stub();

    [LibraryImport("*", EntryPoint = "irq12_stub")]
    private static partial void* irq12_stub();

    [LibraryImport("*", EntryPoint = "irq13_stub")]
    private static partial void* irq13_stub();

    [LibraryImport("*", EntryPoint = "irq14_stub")]
    private static partial void* irq14_stub();

    [LibraryImport("*", EntryPoint = "irq15_stub")]
    private static partial void* irq15_stub();

    [LibraryImport("*", EntryPoint = "irq16_stub")]
    private static partial void* irq16_stub();

    [LibraryImport("*", EntryPoint = "irq17_stub")]
    private static partial void* irq17_stub();

    [LibraryImport("*", EntryPoint = "irq18_stub")]
    private static partial void* irq18_stub();

    [LibraryImport("*", EntryPoint = "irq19_stub")]
    private static partial void* irq19_stub();

    [LibraryImport("*", EntryPoint = "irq20_stub")]
    private static partial void* irq20_stub();

    [LibraryImport("*", EntryPoint = "irq21_stub")]
    private static partial void* irq21_stub();

    [LibraryImport("*", EntryPoint = "irq22_stub")]
    private static partial void* irq22_stub();

    [LibraryImport("*", EntryPoint = "irq23_stub")]
    private static partial void* irq23_stub();

    [LibraryImport("*", EntryPoint = "irq24_stub")]
    private static partial void* irq24_stub();

    [LibraryImport("*", EntryPoint = "irq25_stub")]
    private static partial void* irq25_stub();

    [LibraryImport("*", EntryPoint = "irq26_stub")]
    private static partial void* irq26_stub();

    [LibraryImport("*", EntryPoint = "irq27_stub")]
    private static partial void* irq27_stub();

    [LibraryImport("*", EntryPoint = "irq28_stub")]
    private static partial void* irq28_stub();

    [LibraryImport("*", EntryPoint = "irq29_stub")]
    private static partial void* irq29_stub();

    [LibraryImport("*", EntryPoint = "irq30_stub")]
    private static partial void* irq30_stub();

    [LibraryImport("*", EntryPoint = "irq31_stub")]
    private static partial void* irq31_stub();

    [LibraryImport("*", EntryPoint = "irq32_stub")]
    private static partial void* irq32_stub();

    [LibraryImport("*", EntryPoint = "irq33_stub")]
    private static partial void* irq33_stub();

    [LibraryImport("*", EntryPoint = "irq34_stub")]
    private static partial void* irq34_stub();

    [LibraryImport("*", EntryPoint = "irq35_stub")]
    private static partial void* irq35_stub();

    [LibraryImport("*", EntryPoint = "irq36_stub")]
    private static partial void* irq36_stub();

    [LibraryImport("*", EntryPoint = "irq37_stub")]
    private static partial void* irq37_stub();

    [LibraryImport("*", EntryPoint = "irq38_stub")]
    private static partial void* irq38_stub();

    [LibraryImport("*", EntryPoint = "irq39_stub")]
    private static partial void* irq39_stub();

    [LibraryImport("*", EntryPoint = "irq40_stub")]
    private static partial void* irq40_stub();

    [LibraryImport("*", EntryPoint = "irq41_stub")]
    private static partial void* irq41_stub();

    [LibraryImport("*", EntryPoint = "irq42_stub")]
    private static partial void* irq42_stub();

    [LibraryImport("*", EntryPoint = "irq43_stub")]
    private static partial void* irq43_stub();

    [LibraryImport("*", EntryPoint = "irq44_stub")]
    private static partial void* irq44_stub();

    [LibraryImport("*", EntryPoint = "irq45_stub")]
    private static partial void* irq45_stub();

    [LibraryImport("*", EntryPoint = "irq46_stub")]
    private static partial void* irq46_stub();

    [LibraryImport("*", EntryPoint = "irq47_stub")]
    private static partial void* irq47_stub();

    [LibraryImport("*", EntryPoint = "irq48_stub")]
    private static partial void* irq48_stub();

    [LibraryImport("*", EntryPoint = "irq49_stub")]
    private static partial void* irq49_stub();

    [LibraryImport("*", EntryPoint = "irq50_stub")]
    private static partial void* irq50_stub();

    [LibraryImport("*", EntryPoint = "irq51_stub")]
    private static partial void* irq51_stub();

    [LibraryImport("*", EntryPoint = "irq52_stub")]
    private static partial void* irq52_stub();

    [LibraryImport("*", EntryPoint = "irq53_stub")]
    private static partial void* irq53_stub();

    [LibraryImport("*", EntryPoint = "irq54_stub")]
    private static partial void* irq54_stub();

    [LibraryImport("*", EntryPoint = "irq55_stub")]
    private static partial void* irq55_stub();

    [LibraryImport("*", EntryPoint = "irq56_stub")]
    private static partial void* irq56_stub();

    [LibraryImport("*", EntryPoint = "irq57_stub")]
    private static partial void* irq57_stub();

    [LibraryImport("*", EntryPoint = "irq58_stub")]
    private static partial void* irq58_stub();

    [LibraryImport("*", EntryPoint = "irq59_stub")]
    private static partial void* irq59_stub();

    [LibraryImport("*", EntryPoint = "irq60_stub")]
    private static partial void* irq60_stub();

    [LibraryImport("*", EntryPoint = "irq61_stub")]
    private static partial void* irq61_stub();

    [LibraryImport("*", EntryPoint = "irq62_stub")]
    private static partial void* irq62_stub();

    [LibraryImport("*", EntryPoint = "irq63_stub")]
    private static partial void* irq63_stub();

    [LibraryImport("*", EntryPoint = "irq64_stub")]
    private static partial void* irq64_stub();

    [LibraryImport("*", EntryPoint = "irq65_stub")]
    private static partial void* irq65_stub();

    [LibraryImport("*", EntryPoint = "irq66_stub")]
    private static partial void* irq66_stub();

    [LibraryImport("*", EntryPoint = "irq67_stub")]
    private static partial void* irq67_stub();

    [LibraryImport("*", EntryPoint = "irq68_stub")]
    private static partial void* irq68_stub();

    [LibraryImport("*", EntryPoint = "irq69_stub")]
    private static partial void* irq69_stub();

    [LibraryImport("*", EntryPoint = "irq70_stub")]
    private static partial void* irq70_stub();

    [LibraryImport("*", EntryPoint = "irq71_stub")]
    private static partial void* irq71_stub();

    [LibraryImport("*", EntryPoint = "irq72_stub")]
    private static partial void* irq72_stub();

    [LibraryImport("*", EntryPoint = "irq73_stub")]
    private static partial void* irq73_stub();

    [LibraryImport("*", EntryPoint = "irq74_stub")]
    private static partial void* irq74_stub();

    [LibraryImport("*", EntryPoint = "irq75_stub")]
    private static partial void* irq75_stub();

    [LibraryImport("*", EntryPoint = "irq76_stub")]
    private static partial void* irq76_stub();

    [LibraryImport("*", EntryPoint = "irq77_stub")]
    private static partial void* irq77_stub();

    [LibraryImport("*", EntryPoint = "irq78_stub")]
    private static partial void* irq78_stub();

    [LibraryImport("*", EntryPoint = "irq79_stub")]
    private static partial void* irq79_stub();

    [LibraryImport("*", EntryPoint = "irq80_stub")]
    private static partial void* irq80_stub();

    [LibraryImport("*", EntryPoint = "irq81_stub")]
    private static partial void* irq81_stub();

    [LibraryImport("*", EntryPoint = "irq82_stub")]
    private static partial void* irq82_stub();

    [LibraryImport("*", EntryPoint = "irq83_stub")]
    private static partial void* irq83_stub();

    [LibraryImport("*", EntryPoint = "irq84_stub")]
    private static partial void* irq84_stub();

    [LibraryImport("*", EntryPoint = "irq85_stub")]
    private static partial void* irq85_stub();

    [LibraryImport("*", EntryPoint = "irq86_stub")]
    private static partial void* irq86_stub();

    [LibraryImport("*", EntryPoint = "irq87_stub")]
    private static partial void* irq87_stub();

    [LibraryImport("*", EntryPoint = "irq88_stub")]
    private static partial void* irq88_stub();

    [LibraryImport("*", EntryPoint = "irq89_stub")]
    private static partial void* irq89_stub();

    [LibraryImport("*", EntryPoint = "irq90_stub")]
    private static partial void* irq90_stub();

    [LibraryImport("*", EntryPoint = "irq91_stub")]
    private static partial void* irq91_stub();

    [LibraryImport("*", EntryPoint = "irq92_stub")]
    private static partial void* irq92_stub();

    [LibraryImport("*", EntryPoint = "irq93_stub")]
    private static partial void* irq93_stub();

    [LibraryImport("*", EntryPoint = "irq94_stub")]
    private static partial void* irq94_stub();

    [LibraryImport("*", EntryPoint = "irq95_stub")]
    private static partial void* irq95_stub();

    [LibraryImport("*", EntryPoint = "irq96_stub")]
    private static partial void* irq96_stub();

    [LibraryImport("*", EntryPoint = "irq97_stub")]
    private static partial void* irq97_stub();

    [LibraryImport("*", EntryPoint = "irq98_stub")]
    private static partial void* irq98_stub();

    [LibraryImport("*", EntryPoint = "irq99_stub")]
    private static partial void* irq99_stub();

    [LibraryImport("*", EntryPoint = "irq100_stub")]
    private static partial void* irq100_stub();

    [LibraryImport("*", EntryPoint = "irq101_stub")]
    private static partial void* irq101_stub();

    [LibraryImport("*", EntryPoint = "irq102_stub")]
    private static partial void* irq102_stub();

    [LibraryImport("*", EntryPoint = "irq103_stub")]
    private static partial void* irq103_stub();

    [LibraryImport("*", EntryPoint = "irq104_stub")]
    private static partial void* irq104_stub();

    [LibraryImport("*", EntryPoint = "irq105_stub")]
    private static partial void* irq105_stub();

    [LibraryImport("*", EntryPoint = "irq106_stub")]
    private static partial void* irq106_stub();

    [LibraryImport("*", EntryPoint = "irq107_stub")]
    private static partial void* irq107_stub();

    [LibraryImport("*", EntryPoint = "irq108_stub")]
    private static partial void* irq108_stub();

    [LibraryImport("*", EntryPoint = "irq109_stub")]
    private static partial void* irq109_stub();

    [LibraryImport("*", EntryPoint = "irq110_stub")]
    private static partial void* irq110_stub();

    [LibraryImport("*", EntryPoint = "irq111_stub")]
    private static partial void* irq111_stub();

    [LibraryImport("*", EntryPoint = "irq112_stub")]
    private static partial void* irq112_stub();

    [LibraryImport("*", EntryPoint = "irq113_stub")]
    private static partial void* irq113_stub();

    [LibraryImport("*", EntryPoint = "irq114_stub")]
    private static partial void* irq114_stub();

    [LibraryImport("*", EntryPoint = "irq115_stub")]
    private static partial void* irq115_stub();

    [LibraryImport("*", EntryPoint = "irq116_stub")]
    private static partial void* irq116_stub();

    [LibraryImport("*", EntryPoint = "irq117_stub")]
    private static partial void* irq117_stub();

    [LibraryImport("*", EntryPoint = "irq118_stub")]
    private static partial void* irq118_stub();

    [LibraryImport("*", EntryPoint = "irq119_stub")]
    private static partial void* irq119_stub();

    [LibraryImport("*", EntryPoint = "irq120_stub")]
    private static partial void* irq120_stub();

    [LibraryImport("*", EntryPoint = "irq121_stub")]
    private static partial void* irq121_stub();

    [LibraryImport("*", EntryPoint = "irq122_stub")]
    private static partial void* irq122_stub();

    [LibraryImport("*", EntryPoint = "irq123_stub")]
    private static partial void* irq123_stub();

    [LibraryImport("*", EntryPoint = "irq124_stub")]
    private static partial void* irq124_stub();

    [LibraryImport("*", EntryPoint = "irq125_stub")]
    private static partial void* irq125_stub();

    [LibraryImport("*", EntryPoint = "irq126_stub")]
    private static partial void* irq126_stub();

    [LibraryImport("*", EntryPoint = "irq127_stub")]
    private static partial void* irq127_stub();

    [LibraryImport("*", EntryPoint = "irq128_stub")]
    private static partial void* irq128_stub();

    [LibraryImport("*", EntryPoint = "irq129_stub")]
    private static partial void* irq129_stub();

    [LibraryImport("*", EntryPoint = "irq130_stub")]
    private static partial void* irq130_stub();

    [LibraryImport("*", EntryPoint = "irq131_stub")]
    private static partial void* irq131_stub();

    [LibraryImport("*", EntryPoint = "irq132_stub")]
    private static partial void* irq132_stub();

    [LibraryImport("*", EntryPoint = "irq133_stub")]
    private static partial void* irq133_stub();

    [LibraryImport("*", EntryPoint = "irq134_stub")]
    private static partial void* irq134_stub();

    [LibraryImport("*", EntryPoint = "irq135_stub")]
    private static partial void* irq135_stub();

    [LibraryImport("*", EntryPoint = "irq136_stub")]
    private static partial void* irq136_stub();

    [LibraryImport("*", EntryPoint = "irq137_stub")]
    private static partial void* irq137_stub();

    [LibraryImport("*", EntryPoint = "irq138_stub")]
    private static partial void* irq138_stub();

    [LibraryImport("*", EntryPoint = "irq139_stub")]
    private static partial void* irq139_stub();

    [LibraryImport("*", EntryPoint = "irq140_stub")]
    private static partial void* irq140_stub();

    [LibraryImport("*", EntryPoint = "irq141_stub")]
    private static partial void* irq141_stub();

    [LibraryImport("*", EntryPoint = "irq142_stub")]
    private static partial void* irq142_stub();

    [LibraryImport("*", EntryPoint = "irq143_stub")]
    private static partial void* irq143_stub();

    [LibraryImport("*", EntryPoint = "irq144_stub")]
    private static partial void* irq144_stub();

    [LibraryImport("*", EntryPoint = "irq145_stub")]
    private static partial void* irq145_stub();

    [LibraryImport("*", EntryPoint = "irq146_stub")]
    private static partial void* irq146_stub();

    [LibraryImport("*", EntryPoint = "irq147_stub")]
    private static partial void* irq147_stub();

    [LibraryImport("*", EntryPoint = "irq148_stub")]
    private static partial void* irq148_stub();

    [LibraryImport("*", EntryPoint = "irq149_stub")]
    private static partial void* irq149_stub();

    [LibraryImport("*", EntryPoint = "irq150_stub")]
    private static partial void* irq150_stub();

    [LibraryImport("*", EntryPoint = "irq151_stub")]
    private static partial void* irq151_stub();

    [LibraryImport("*", EntryPoint = "irq152_stub")]
    private static partial void* irq152_stub();

    [LibraryImport("*", EntryPoint = "irq153_stub")]
    private static partial void* irq153_stub();

    [LibraryImport("*", EntryPoint = "irq154_stub")]
    private static partial void* irq154_stub();

    [LibraryImport("*", EntryPoint = "irq155_stub")]
    private static partial void* irq155_stub();

    [LibraryImport("*", EntryPoint = "irq156_stub")]
    private static partial void* irq156_stub();

    [LibraryImport("*", EntryPoint = "irq157_stub")]
    private static partial void* irq157_stub();

    [LibraryImport("*", EntryPoint = "irq158_stub")]
    private static partial void* irq158_stub();

    [LibraryImport("*", EntryPoint = "irq159_stub")]
    private static partial void* irq159_stub();

    [LibraryImport("*", EntryPoint = "irq160_stub")]
    private static partial void* irq160_stub();

    [LibraryImport("*", EntryPoint = "irq161_stub")]
    private static partial void* irq161_stub();

    [LibraryImport("*", EntryPoint = "irq162_stub")]
    private static partial void* irq162_stub();

    [LibraryImport("*", EntryPoint = "irq163_stub")]
    private static partial void* irq163_stub();

    [LibraryImport("*", EntryPoint = "irq164_stub")]
    private static partial void* irq164_stub();

    [LibraryImport("*", EntryPoint = "irq165_stub")]
    private static partial void* irq165_stub();

    [LibraryImport("*", EntryPoint = "irq166_stub")]
    private static partial void* irq166_stub();

    [LibraryImport("*", EntryPoint = "irq167_stub")]
    private static partial void* irq167_stub();

    [LibraryImport("*", EntryPoint = "irq168_stub")]
    private static partial void* irq168_stub();

    [LibraryImport("*", EntryPoint = "irq169_stub")]
    private static partial void* irq169_stub();

    [LibraryImport("*", EntryPoint = "irq170_stub")]
    private static partial void* irq170_stub();

    [LibraryImport("*", EntryPoint = "irq171_stub")]
    private static partial void* irq171_stub();

    [LibraryImport("*", EntryPoint = "irq172_stub")]
    private static partial void* irq172_stub();

    [LibraryImport("*", EntryPoint = "irq173_stub")]
    private static partial void* irq173_stub();

    [LibraryImport("*", EntryPoint = "irq174_stub")]
    private static partial void* irq174_stub();

    [LibraryImport("*", EntryPoint = "irq175_stub")]
    private static partial void* irq175_stub();

    [LibraryImport("*", EntryPoint = "irq176_stub")]
    private static partial void* irq176_stub();

    [LibraryImport("*", EntryPoint = "irq177_stub")]
    private static partial void* irq177_stub();

    [LibraryImport("*", EntryPoint = "irq178_stub")]
    private static partial void* irq178_stub();

    [LibraryImport("*", EntryPoint = "irq179_stub")]
    private static partial void* irq179_stub();

    [LibraryImport("*", EntryPoint = "irq180_stub")]
    private static partial void* irq180_stub();

    [LibraryImport("*", EntryPoint = "irq181_stub")]
    private static partial void* irq181_stub();

    [LibraryImport("*", EntryPoint = "irq182_stub")]
    private static partial void* irq182_stub();

    [LibraryImport("*", EntryPoint = "irq183_stub")]
    private static partial void* irq183_stub();

    [LibraryImport("*", EntryPoint = "irq184_stub")]
    private static partial void* irq184_stub();

    [LibraryImport("*", EntryPoint = "irq185_stub")]
    private static partial void* irq185_stub();

    [LibraryImport("*", EntryPoint = "irq186_stub")]
    private static partial void* irq186_stub();

    [LibraryImport("*", EntryPoint = "irq187_stub")]
    private static partial void* irq187_stub();

    [LibraryImport("*", EntryPoint = "irq188_stub")]
    private static partial void* irq188_stub();

    [LibraryImport("*", EntryPoint = "irq189_stub")]
    private static partial void* irq189_stub();

    [LibraryImport("*", EntryPoint = "irq190_stub")]
    private static partial void* irq190_stub();

    [LibraryImport("*", EntryPoint = "irq191_stub")]
    private static partial void* irq191_stub();

    [LibraryImport("*", EntryPoint = "irq192_stub")]
    private static partial void* irq192_stub();

    [LibraryImport("*", EntryPoint = "irq193_stub")]
    private static partial void* irq193_stub();

    [LibraryImport("*", EntryPoint = "irq194_stub")]
    private static partial void* irq194_stub();

    [LibraryImport("*", EntryPoint = "irq195_stub")]
    private static partial void* irq195_stub();

    [LibraryImport("*", EntryPoint = "irq196_stub")]
    private static partial void* irq196_stub();

    [LibraryImport("*", EntryPoint = "irq197_stub")]
    private static partial void* irq197_stub();

    [LibraryImport("*", EntryPoint = "irq198_stub")]
    private static partial void* irq198_stub();

    [LibraryImport("*", EntryPoint = "irq199_stub")]
    private static partial void* irq199_stub();

    [LibraryImport("*", EntryPoint = "irq200_stub")]
    private static partial void* irq200_stub();

    [LibraryImport("*", EntryPoint = "irq201_stub")]
    private static partial void* irq201_stub();

    [LibraryImport("*", EntryPoint = "irq202_stub")]
    private static partial void* irq202_stub();

    [LibraryImport("*", EntryPoint = "irq203_stub")]
    private static partial void* irq203_stub();

    [LibraryImport("*", EntryPoint = "irq204_stub")]
    private static partial void* irq204_stub();

    [LibraryImport("*", EntryPoint = "irq205_stub")]
    private static partial void* irq205_stub();

    [LibraryImport("*", EntryPoint = "irq206_stub")]
    private static partial void* irq206_stub();

    [LibraryImport("*", EntryPoint = "irq207_stub")]
    private static partial void* irq207_stub();

    [LibraryImport("*", EntryPoint = "irq208_stub")]
    private static partial void* irq208_stub();

    [LibraryImport("*", EntryPoint = "irq209_stub")]
    private static partial void* irq209_stub();

    [LibraryImport("*", EntryPoint = "irq210_stub")]
    private static partial void* irq210_stub();

    [LibraryImport("*", EntryPoint = "irq211_stub")]
    private static partial void* irq211_stub();

    [LibraryImport("*", EntryPoint = "irq212_stub")]
    private static partial void* irq212_stub();

    [LibraryImport("*", EntryPoint = "irq213_stub")]
    private static partial void* irq213_stub();

    [LibraryImport("*", EntryPoint = "irq214_stub")]
    private static partial void* irq214_stub();

    [LibraryImport("*", EntryPoint = "irq215_stub")]
    private static partial void* irq215_stub();

    [LibraryImport("*", EntryPoint = "irq216_stub")]
    private static partial void* irq216_stub();

    [LibraryImport("*", EntryPoint = "irq217_stub")]
    private static partial void* irq217_stub();

    [LibraryImport("*", EntryPoint = "irq218_stub")]
    private static partial void* irq218_stub();

    [LibraryImport("*", EntryPoint = "irq219_stub")]
    private static partial void* irq219_stub();

    [LibraryImport("*", EntryPoint = "irq220_stub")]
    private static partial void* irq220_stub();

    [LibraryImport("*", EntryPoint = "irq221_stub")]
    private static partial void* irq221_stub();

    [LibraryImport("*", EntryPoint = "irq222_stub")]
    private static partial void* irq222_stub();

    [LibraryImport("*", EntryPoint = "irq223_stub")]
    private static partial void* irq223_stub();

    [LibraryImport("*", EntryPoint = "irq224_stub")]
    private static partial void* irq224_stub();

    [LibraryImport("*", EntryPoint = "irq225_stub")]
    private static partial void* irq225_stub();

    [LibraryImport("*", EntryPoint = "irq226_stub")]
    private static partial void* irq226_stub();

    [LibraryImport("*", EntryPoint = "irq227_stub")]
    private static partial void* irq227_stub();

    [LibraryImport("*", EntryPoint = "irq228_stub")]
    private static partial void* irq228_stub();

    [LibraryImport("*", EntryPoint = "irq229_stub")]
    private static partial void* irq229_stub();

    [LibraryImport("*", EntryPoint = "irq230_stub")]
    private static partial void* irq230_stub();

    [LibraryImport("*", EntryPoint = "irq231_stub")]
    private static partial void* irq231_stub();

    [LibraryImport("*", EntryPoint = "irq232_stub")]
    private static partial void* irq232_stub();

    [LibraryImport("*", EntryPoint = "irq233_stub")]
    private static partial void* irq233_stub();

    [LibraryImport("*", EntryPoint = "irq234_stub")]
    private static partial void* irq234_stub();

    [LibraryImport("*", EntryPoint = "irq235_stub")]
    private static partial void* irq235_stub();

    [LibraryImport("*", EntryPoint = "irq236_stub")]
    private static partial void* irq236_stub();

    [LibraryImport("*", EntryPoint = "irq237_stub")]
    private static partial void* irq237_stub();

    [LibraryImport("*", EntryPoint = "irq238_stub")]
    private static partial void* irq238_stub();

    [LibraryImport("*", EntryPoint = "irq239_stub")]
    private static partial void* irq239_stub();

    [LibraryImport("*", EntryPoint = "irq240_stub")]
    private static partial void* irq240_stub();

    [LibraryImport("*", EntryPoint = "irq241_stub")]
    private static partial void* irq241_stub();

    [LibraryImport("*", EntryPoint = "irq242_stub")]
    private static partial void* irq242_stub();

    [LibraryImport("*", EntryPoint = "irq243_stub")]
    private static partial void* irq243_stub();

    [LibraryImport("*", EntryPoint = "irq244_stub")]
    private static partial void* irq244_stub();

    [LibraryImport("*", EntryPoint = "irq245_stub")]
    private static partial void* irq245_stub();

    [LibraryImport("*", EntryPoint = "irq246_stub")]
    private static partial void* irq246_stub();

    [LibraryImport("*", EntryPoint = "irq247_stub")]
    private static partial void* irq247_stub();

    [LibraryImport("*", EntryPoint = "irq248_stub")]
    private static partial void* irq248_stub();

    [LibraryImport("*", EntryPoint = "irq249_stub")]
    private static partial void* irq249_stub();

    [LibraryImport("*", EntryPoint = "irq250_stub")]
    private static partial void* irq250_stub();

    [LibraryImport("*", EntryPoint = "irq251_stub")]
    private static partial void* irq251_stub();

    [LibraryImport("*", EntryPoint = "irq252_stub")]
    private static partial void* irq252_stub();

    [LibraryImport("*", EntryPoint = "irq253_stub")]
    private static partial void* irq253_stub();

    [LibraryImport("*", EntryPoint = "irq254_stub")]
    private static partial void* irq254_stub();

    [LibraryImport("*", EntryPoint = "irq255_stub")]
    private static partial void* irq255_stub();
}
