# Use the base image for .NET development
FROM mcr.microsoft.com/devcontainers/dotnet:10.0-noble

# Install clang, xorriso, lld, and yasm
RUN apt-get update && apt-get install -y \
            clang \
            gcc-aarch64-linux-gnu \
            binutils-aarch64-linux-gnu \
            xorriso \
            lld \
            llvm \
            yasm \
            gdb \
            gdb-multiarch \
            qemu-system-x86 \
            qemu-system-aarch64 \
            qemu-utils \
            && apt-get clean
