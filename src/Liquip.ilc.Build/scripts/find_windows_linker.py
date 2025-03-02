import subprocess
import os
import sys

linkers = [
    "link.exe",      # MSVC: Microsoft’s native linker for Visual Studio.
    "lld-link.exe",   # LLVM/Clang: LLVM’s Windows linker (optimized for Windows targets).
    "ld.exe",         # MinGW/GCC: GNU linker used in GCC-based toolchains on Windows.
    "xilink.exe",     # Intel C++: Intel’s variant of the MSVC linker for Windows.
    "armlink.exe",    # ARM Compiler: Linker for ARM toolchains (e.g., Keil) targeting Windows.
    "ld.lld.exe",     # LLVM (Unix-like naming): LLVM’s linker compiled for Windows.
    "gold.exe"        # MinGW (optional): GNU’s Gold linker alternative for Windows (less common).
]

def find_linker():
    for linker in linkers:
        try:
            return subprocess.check_output(["where",linker])
        except subprocess.CalledProcessError as e:
            print(f"{linker} linker not found. Skipping")
            continue
    return None

if __name__ == "__main__":
   linker_path = find_linker()
   if linker_path:
       print(linker_path) # For MSBuild
   else:
        print("No linkers could be found")
        sys.exit(0)

