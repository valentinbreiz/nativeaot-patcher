# Installing the Cosmos VS Code Extension

The easiest way to use gen3 is through the [Cosmos VS Code Extension](https://github.com/valentinbreiz/CosmosVsCodeExtension). The `Cosmos.Tools` .NET global tool handles the installation of the extension and its dependencies.

## Prerequisites

- [.NET SDK](https://dotnet.microsoft.com/download) 10.0 or later
- [Visual Studio Code](https://code.visualstudio.com/)

## Steps

### 1. Install Cosmos.Tools

Install the `Cosmos.Tools` .NET global tool:

```bash
dotnet tool install -g Cosmos.Tools
```

If you already have it installed and want to update it:

```bash
dotnet tool update -g Cosmos.Tools
```

### 2. Check Dependencies

Run `cosmos check` to verify that all required dependencies are installed and properly configured on your system:

```bash
cosmos check
```

This command will report any missing tools or configuration issues that need to be resolved before proceeding.

### 3. Install the VS Code Extension

Run `cosmos install` to download and install the Cosmos VS Code Extension:

```bash
cosmos install
```

This will install the extension into VS Code automatically.

### 4. Start Using Cosmos

Once installed, open VS Code and use the Cosmos extension to create, build, and run bare-metal C# kernels directly from the editor.
