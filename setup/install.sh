#!/usr/bin/env bash
# Cosmos OS Development Kit - Offline Installer for Linux/macOS
# Extracts bundled tools to ~/.cosmos/ and configures the environment.
#
# Usage:
#   tar xzf cosmos-<version>-<platform>.tar.gz
#   cd cosmos-<version>-<platform>
#   ./install.sh
#
# Uninstall:
#   ./install.sh --uninstall

set -euo pipefail

COSMOS_HOME="${COSMOS_HOME:-$HOME/.cosmos}"
COSMOS_TOOLS="$COSMOS_HOME/tools"
COSMOS_PACKAGES="$COSMOS_HOME/packages"
COSMOS_DOTNET_TOOLS="$COSMOS_HOME/dotnet-tools"
COSMOS_EXTENSIONS="$COSMOS_HOME/extensions"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BUNDLE_DIR="$SCRIPT_DIR/bundle"

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

info()  { echo -e "${GREEN}[INFO]${NC} $*"; }
warn()  { echo -e "${YELLOW}[WARN]${NC} $*"; }
error() { echo -e "${RED}[ERROR]${NC} $*" >&2; }

# Detect OS
detect_os() {
    case "$(uname -s)" in
        Linux*)  echo "linux" ;;
        Darwin*) echo "macos" ;;
        *)       echo "unknown" ;;
    esac
}

# Detect architecture
detect_arch() {
    case "$(uname -m)" in
        x86_64|amd64) echo "x64" ;;
        aarch64|arm64) echo "arm64" ;;
        *)             echo "unknown" ;;
    esac
}

# Check if .NET SDK is installed
check_dotnet() {
    if ! command -v dotnet &>/dev/null; then
        error ".NET SDK is not installed."
        echo "  Please install .NET 10.0 SDK from https://dot.net/download"
        echo "  or via your package manager."
        return 1
    fi

    local version
    version=$(dotnet --version 2>/dev/null || echo "0")
    local major="${version%%.*}"
    if [[ "$major" -lt 10 ]]; then
        warn ".NET SDK version $version detected. Cosmos requires .NET 10.0+."
        echo "  Download from https://dot.net/download"
    fi
    info ".NET SDK found: $version"
}

# Get the shell profile file to modify
get_shell_profile() {
    local shell_name
    shell_name="$(basename "${SHELL:-/bin/bash}")"
    case "$shell_name" in
        zsh)  echo "$HOME/.zshrc" ;;
        bash)
            if [[ -f "$HOME/.bash_profile" ]]; then
                echo "$HOME/.bash_profile"
            else
                echo "$HOME/.bashrc"
            fi
            ;;
        fish) echo "$HOME/.config/fish/config.fish" ;;
        *)    echo "$HOME/.profile" ;;
    esac
}

# Add a line to the shell profile if not already present
add_to_profile() {
    local profile="$1"
    local line="$2"
    local comment="$3"

    if [[ -f "$profile" ]] && grep -qF "$line" "$profile" 2>/dev/null; then
        return 0  # Already present
    fi

    echo "" >> "$profile"
    echo "# $comment" >> "$profile"
    echo "$line" >> "$profile"
}

# Remove Cosmos lines from shell profile
remove_from_profile() {
    local profile="$1"
    if [[ ! -f "$profile" ]]; then
        return 0
    fi

    # Remove lines containing cosmos path markers
    local tmp
    tmp=$(mktemp)
    grep -v '# Cosmos OS Development Kit' "$profile" | grep -v '\.cosmos/tools/bin' > "$tmp" || true
    mv "$tmp" "$profile"
}

# ─── Install ───────────────────────────────────────────────────────────────────

do_install() {
    local os arch
    os=$(detect_os)
    arch=$(detect_arch)

    echo "============================================"
    echo "  Cosmos OS Development Kit - Installer"
    echo "============================================"
    echo "  OS:           $os"
    echo "  Architecture: $arch"
    echo "  Install to:   $COSMOS_HOME"
    echo "============================================"
    echo ""

    # Preflight checks
    check_dotnet || {
        read -rp "Continue without .NET SDK? [y/N] " ans
        [[ "$ans" =~ ^[Yy] ]] || exit 1
    }

    if [[ ! -d "$BUNDLE_DIR" ]]; then
        error "Bundle directory not found at $BUNDLE_DIR"
        error "Run this script from within the extracted installer archive."
        exit 1
    fi

    # Create directories
    info "Creating directories..."
    mkdir -p "$COSMOS_TOOLS/bin"
    mkdir -p "$COSMOS_PACKAGES"
    mkdir -p "$COSMOS_DOTNET_TOOLS"
    mkdir -p "$COSMOS_EXTENSIONS"

    # Copy NuGet packages
    if [[ -d "$BUNDLE_DIR/packages" ]]; then
        info "Installing NuGet packages..."
        cp -f "$BUNDLE_DIR/packages/"*.nupkg "$COSMOS_PACKAGES/" 2>/dev/null || true
        local pkg_count
        pkg_count=$(find "$COSMOS_PACKAGES" -name "*.nupkg" | wc -l)
        info "  Installed $pkg_count packages"
    fi

    # Copy dotnet tool packages
    if [[ -d "$BUNDLE_DIR/dotnet-tools" ]]; then
        info "Installing dotnet tool packages..."
        cp -f "$BUNDLE_DIR/dotnet-tools/"*.nupkg "$COSMOS_DOTNET_TOOLS/" 2>/dev/null || true
    fi

    # Copy VS Code extension
    if [[ -d "$BUNDLE_DIR/extensions" ]]; then
        info "Installing VS Code extension..."
        cp -f "$BUNDLE_DIR/extensions/"*.vsix "$COSMOS_EXTENSIONS/" 2>/dev/null || true
    fi

    # Install platform-specific tools
    local tools_src="$BUNDLE_DIR/tools/$os"
    if [[ -d "$tools_src" ]]; then
        info "Installing build tools..."
        cp -rf "$tools_src/"* "$COSMOS_TOOLS/" 2>/dev/null || true

        # Make all binaries executable
        find "$COSMOS_TOOLS" -type f \( -name "*.exe" -o ! -name "*.*" \) -exec chmod +x {} + 2>/dev/null || true

        # Create symlinks in bin/ for tool discovery
        for tool_dir in "$COSMOS_TOOLS"/*/; do
            local dir_name
            dir_name=$(basename "$tool_dir")
            [[ "$dir_name" == "bin" ]] && continue

            # Link binaries from tool subdirectories into bin/
            if [[ -d "$tool_dir/bin" ]]; then
                for bin in "$tool_dir/bin/"*; do
                    [[ -f "$bin" ]] || continue
                    ln -sf "$bin" "$COSMOS_TOOLS/bin/$(basename "$bin")" 2>/dev/null || true
                done
            else
                # Single-binary tools (e.g., yasm, xorriso)
                for bin in "$tool_dir"/*; do
                    [[ -x "$bin" && -f "$bin" ]] || continue
                    ln -sf "$bin" "$COSMOS_TOOLS/bin/$(basename "$bin")" 2>/dev/null || true
                done
            fi
        done

        info "  Tools installed to $COSMOS_TOOLS"
    else
        warn "No bundled tools found for $os. You may need to install them manually."
        warn "  Run: cosmos install"
    fi

    # Register NuGet local feed
    info "Registering Cosmos NuGet feed..."
    dotnet nuget remove source "Cosmos Local Feed" &>/dev/null || true
    dotnet nuget add source "$COSMOS_PACKAGES" --name "Cosmos Local Feed" || {
        warn "Failed to register NuGet feed. You can add it manually:"
        warn "  dotnet nuget add source \"$COSMOS_PACKAGES\" --name \"Cosmos Local Feed\""
    }

    # Install dotnet global tools
    info "Installing Cosmos Patcher..."
    dotnet tool install -g Cosmos.Patcher --add-source "$COSMOS_DOTNET_TOOLS" 2>/dev/null || \
        dotnet tool update -g Cosmos.Patcher --add-source "$COSMOS_DOTNET_TOOLS" 2>/dev/null || \
        warn "Failed to install Cosmos.Patcher. Install manually: dotnet tool install -g Cosmos.Patcher"

    info "Installing Cosmos Tools CLI..."
    dotnet tool install -g Cosmos.Tools --add-source "$COSMOS_DOTNET_TOOLS" 2>/dev/null || \
        dotnet tool update -g Cosmos.Tools --add-source "$COSMOS_DOTNET_TOOLS" 2>/dev/null || \
        warn "Failed to install Cosmos.Tools. Install manually: dotnet tool install -g Cosmos.Tools"

    # Install project templates
    info "Installing project templates..."
    dotnet new install Cosmos.Build.Templates --add-source "$COSMOS_DOTNET_TOOLS" 2>/dev/null || \
        warn "Failed to install templates. Install manually: dotnet new install Cosmos.Build.Templates"

    # Add to PATH
    local profile
    profile=$(get_shell_profile)
    local path_line="export PATH=\"$COSMOS_TOOLS/bin:\$PATH\""
    add_to_profile "$profile" "$path_line" "Cosmos OS Development Kit"
    info "Added $COSMOS_TOOLS/bin to PATH in $profile"

    # Install VS Code extension
    if command -v code &>/dev/null; then
        local vsix
        vsix=$(find "$COSMOS_EXTENSIONS" -name "*.vsix" -print -quit 2>/dev/null)
        if [[ -n "$vsix" ]]; then
            info "Installing VS Code extension..."
            code --install-extension "$vsix" --force 2>/dev/null || \
                warn "Failed to install VS Code extension."
        fi
    else
        info "VS Code not found, skipping extension install."
    fi

    echo ""
    echo "============================================"
    info "Installation complete!"
    echo "============================================"
    echo ""
    echo "  Restart your terminal or run:"
    echo "    source $profile"
    echo ""
    echo "  Then verify with:"
    echo "    cosmos check"
    echo ""
    echo "  Create a new kernel project:"
    echo "    cosmos new MyKernel"
    echo ""
}

# ─── Uninstall ─────────────────────────────────────────────────────────────────

do_uninstall() {
    echo "============================================"
    echo "  Cosmos OS Development Kit - Uninstaller"
    echo "============================================"
    echo ""

    read -rp "Remove Cosmos from $COSMOS_HOME? [y/N] " ans
    [[ "$ans" =~ ^[Yy] ]] || exit 0

    # Remove dotnet tools
    info "Removing dotnet global tools..."
    dotnet tool uninstall -g Cosmos.Patcher 2>/dev/null || true
    dotnet tool uninstall -g Cosmos.Tools 2>/dev/null || true
    dotnet new uninstall Cosmos.Build.Templates 2>/dev/null || true

    # Remove NuGet feed
    info "Removing NuGet feed..."
    dotnet nuget remove source "Cosmos Local Feed" 2>/dev/null || true

    # Remove from PATH
    local profile
    profile=$(get_shell_profile)
    remove_from_profile "$profile"
    info "Removed Cosmos from $profile"

    # Remove install directory
    if [[ -d "$COSMOS_HOME" ]]; then
        info "Removing $COSMOS_HOME..."
        rm -rf "$COSMOS_HOME"
    fi

    echo ""
    info "Uninstall complete."
}

# ─── Main ──────────────────────────────────────────────────────────────────────

case "${1:-}" in
    --uninstall|-u)
        do_uninstall
        ;;
    --help|-h)
        echo "Usage: $0 [--uninstall]"
        echo ""
        echo "  Install:    $0"
        echo "  Uninstall:  $0 --uninstall"
        echo ""
        echo "Environment variables:"
        echo "  COSMOS_HOME   Installation directory (default: ~/.cosmos)"
        ;;
    *)
        do_install
        ;;
esac
