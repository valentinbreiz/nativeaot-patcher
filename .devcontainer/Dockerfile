# Use the base image for .NET development
FROM mcr.microsoft.com/devcontainers/dotnet:1-9.0-bookworm

# Install clang, xorriso, lld, and yasm
RUN apt-get update && apt-get install -y clang xorriso lld llvm yasm && apt-get clean
