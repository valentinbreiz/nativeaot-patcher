# Debugging with VSCode and QEMU

## Setting Up Debugging

When debugging the kernel with VSCode and QEMU, you need to manually set breakpoints for the debugging functions.

### Required Manual Breakpoint

Due to limitations with VSCode's remote GDB debugging for kernel development, you must manually set a breakpoint at:

```
_native_debug_breakpoint_soft
```

### How to Set the Breakpoint

**Important:** You must set the breakpoint BEFORE starting the debugging session.

1. Open the RUN AND DEBUG view in VSCode
2. Before the kernel starts executing, set this breakpoint under BREAKPOINTS:
   ```
   _native_debug_breakpoint_soft
   ```
3. Start your debugging session (F5)
4. The kernel will now stop at this breakpoint when it's reached
