# Debugging with VSCode and QEMU

## Setting Up Debugging

Debugging the kernel with VSCode and QEMU uses remote GDB debugging. Breakpoints can be set directly in the editor as with any standard debugging session.

### How to Debug

1. Open the RUN AND DEBUG view in VSCode
2. Set breakpoints anywhere in your kernel source code
3. Start your debugging session (F5) — for example, select **Debug x64 DevKernel** from the RUN AND DEBUG dropdown
4. Qemu will launchs and the kernel will stop at your breakpoints as expected
