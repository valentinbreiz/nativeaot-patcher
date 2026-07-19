# Use the base image for .NET development
FROM mcr.microsoft.com/devcontainers/dotnet:10.0-noble

# Install clang, xorriso, and lld
RUN apt-get update && apt-get install -y \
            clang \
            xorriso \
            lld \
            llvm \
            gdb \
            gdb-multiarch \
            qemu-system-x86 \
            qemu-system-aarch64 \
            qemu-utils \
            && apt-get clean
