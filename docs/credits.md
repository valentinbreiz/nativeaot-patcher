# Credits

Cosmos Gen3 stands on the shoulders of other open-source projects. This page lists the third-party components that ship as part of Cosmos, with their licenses.

## Vendored source code

Code copied into this repository (with local adaptations noted in a `README.md` next to the sources):

| Project | License | Used for | Location |
|---|---|---|---|
| [BigGustave](https://github.com/EliotJones/BigGustave) | Unlicense | PNG decoding (`Cosmos.Kernel.System.Graphics.Png`) | `src/Cosmos.Kernel.System/Graphics/Images/Png/` |
| [SharpZipLib](https://github.com/icsharpcode/SharpZipLib) | MIT | Managed DEFLATE/zlib decompression for the PNG decoder | `src/Cosmos.Kernel.System/IO/Compression/SharpZipLib/` |
| [Spleen](https://github.com/fcambus/spleen) | BSD-2-Clause | The default 16x32 console and canvas font | `src/Cosmos.Kernel.System/Graphics/Fonts/` |

The PNG integration follows [CosmosPNG](https://github.com/Szymekk44/CosmosPNG) (Unlicense), which first brought BigGustave to Cosmos Gen2.

## Shipped binaries and dependencies

| Project | License | Used for |
|---|---|---|
| [Limine](https://github.com/limine-bootloader/limine) | BSD-2-Clause | The bootloader included in every kernel ISO |
| [.NET Runtime](https://github.com/dotnet/runtime) | MIT | The base class libraries and NativeAOT compiler (ILC) the kernel is built on |
| [Mono.Cecil](https://github.com/jbevain/cecil) | MIT | IL reading and rewriting in `cosmos-patcher` |

## Build and test tools

Not distributed with Cosmos, but required to build or test it: [YASM](https://yasm.tortall.net/), GNU binutils/GCC, [xorriso](https://www.gnu.org/software/xorriso/) and [QEMU](https://www.qemu.org/).
