using System;
using System.Runtime;
using System.Runtime.InteropServices;
using Cosmos.Kernel.Boot.Limine;
using Cosmos.Kernel.Core.Memory;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.IO;

unsafe class Program
{
    static readonly LimineFramebufferRequest Framebuffer = new();
    static readonly LimineHHDMRequest HHDM = new();

    [UnmanagedCallersOnly(EntryPoint = "kmain")]
    static void KernelMain() => Main();
    static void Main()
    {
        MemoryOp.InitializeHeap(HHDM.Offset, 0x1000000);
        var fb = Framebuffer.Response->Framebuffers[0];
        Canvas.Address = (uint*)fb->Address;
        Canvas.Pitch = (uint)fb->Pitch;
        Canvas.Width = (uint)fb->Width;
        Canvas.Height = (uint)fb->Height;

        Canvas.ClearScreen(Color.Black);

        Canvas.DrawString("CosmosOS booted.", 0, 0, Color.White);

        Serial.ComInit();

        Canvas.DrawString("UART started.", 0, 28, Color.White);

        Serial.WriteString("Hello from UART\n");

        while (true) ;
    }
}

namespace Internal.Runtime.CompilerHelpers
{
    // A class that the compiler looks for that has helpers to initialize the
    // process. The compiler can gracefully handle the helpers not being present,
    // but the class itself being absent is unhandled. Let's add an empty class.
    class StartupCodeHelpers
    {
        // A couple symbols the generated code will need we park them in this class
        // for no particular reason. These aid in transitioning to/from managed code.
        // Since we don't have a GC, the transition is a no-op.
        [RuntimeExport("RhpReversePInvoke")]
        static void RhpReversePInvoke(IntPtr frame) { }
        [RuntimeExport("RhpReversePInvokeReturn")]
        static void RhpReversePInvokeReturn(IntPtr frame) { }
        [RuntimeExport("RhpPInvoke")]
        static void RhpPInvoke(IntPtr frame) { }
        [RuntimeExport("RhpPInvokeReturn")]
        static void RhpPInvokeReturn(IntPtr frame) { }

        [RuntimeExport("RhpFallbackFailFast")]
        static void RhpFallbackFailFast() { while (true) ; }

        //[RuntimeExport("InitializeModules")]
        //static unsafe void InitializeModules(IntPtr osModule, IntPtr* pModuleHeaders, int count, IntPtr* pClasslibFunctions, int nClasslibFunctions) { }

    }

    public static class ThrowHelpers
    {
        public static void ThrowNotImplementedException()
        {
            while (true) ;
        }

        public static void ThrowNullReferenceException()
        {
            while (true) ;
        }

        public static void ThrowIndexOutOfRangeException()
        {
            while (true) ;
        }

        public static void ThrowInvalidProgramException()
        {
            while (true) ;
        }

        public static void ThrowTypeLoadException()
        {
            while (true) ;
        }

        public static void ThrowTypeLoadExceptionWithArgument()
        {
            while (true) ;
        }

        public static void ThrowInvalidProgramExceptionWithArgument()
        {
            while (true) ;
        }

        public static void ThrowOverflowException()
        {
            while (true) ;
        }
    }
}