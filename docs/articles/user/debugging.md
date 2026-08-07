# Debugging with VS Code and QEMU

Kernel debugging uses remote GDB: QEMU exposes a GDB server on `localhost:1234`, VS Code connects to it with `cppdbg`, and breakpoints are set directly in the editor. The `cosmos new` template ships the required `.vscode/launch.json` and `.vscode/tasks.json` preconfigured.

---

## Prerequisites

| Tool | Purpose | Notes |
|------|---------|-------|
| `gdb` | x64 debugging | Must be on `PATH`; the launch config invokes it as `gdb` |
| `gdb-multiarch` | ARM64 debugging | Must be on `PATH`; required for aarch64 targets |
| QEMU | Runs the kernel | Installed by `cosmos install` |
| VS Code C/C++ extension | `cppdbg` debug adapter | `ms-vscode.cpptools` |

`cosmos check` verifies the toolchain. On Debian/Ubuntu, `apt install gdb gdb-multiarch` covers both debuggers.

---

## Debugging a kernel created with `cosmos new`

1. Open the kernel folder in VS Code.
2. Set breakpoints in your kernel source.
3. Open the **Run and Debug** view and select **Debug x64 Kernel** (or **Debug ARM64 Kernel**).
4. Press F5. The pre-launch task builds the kernel, starts QEMU with `-s -S` (GDB server, frozen at startup), and the debugger attaches. Execution stops at your breakpoints.

The configuration names above are what the template generates. When working on the framework repository itself, the equivalent configurations are named **Debug x64 DevKernel** / **Debug ARM64 DevKernel**.

---

## Serial log

Every kernel boot phase logs to the serial port (COM1); `cosmos run` and `make run` connect it to your terminal. When a kernel does not come up, or crashes before the debugger is useful, the serial log is the first thing to read — see [Kernel Startup](startup.md) for a phase-by-phase walkthrough and how to symbolicate crash addresses.

---

## Known limitations

- Source-link and variable-inspection bugs exist in the VS Code debugging experience (see the [roadmap](../../roadmap.md)); stepping and breakpoints work, but inspecting some locals can show wrong or missing values.
- Debugging assumes QEMU. VMware, VirtualBox and Hyper-V are untested targets.
- ARM64 debugging under TCG emulation is slow; expect multi-second pauses on step operations on large kernels.
