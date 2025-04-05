# Use the base image for .NET development
FROM mcr.microsoft.com/devcontainers/dotnet:1-8.0-bookworm

# Install clang
RUN apt-get update && apt-get install -y clang xorriso lld && apt-get clean