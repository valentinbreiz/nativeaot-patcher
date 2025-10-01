#!/bin/bash
# Automated test runner for Cosmos.Tests.Kernel
# Builds the test kernel and runs it in QEMU, parsing test results

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Configuration
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
PROJECT_FILE="$SCRIPT_DIR/Cosmos.Tests.Kernel.csproj"
OUTPUT_DIR="$PROJECT_ROOT/output-tests"
LOG_FILE="$PROJECT_ROOT/tests.log"
ARCH="${1:-x64}"  # Default to x64, allow override
TIMEOUT="${2:-10}" # Default 10 second timeout

# Validate architecture
if [[ "$ARCH" != "x64" && "$ARCH" != "arm64" ]]; then
    echo -e "${RED}Error: Invalid architecture '$ARCH'. Use 'x64' or 'arm64'.${NC}"
    exit 1
fi

# Set architecture-specific settings
if [[ "$ARCH" == "x64" ]]; then
    RUNTIME_ID="linux-x64"
    DEFINE_CONSTANTS="ARCH_X64"
    QEMU_BIN="qemu-system-x86_64"
    QEMU_ARGS="-m 512M"
elif [[ "$ARCH" == "arm64" ]]; then
    RUNTIME_ID="linux-arm64"
    DEFINE_CONSTANTS="ARCH_ARM64"
    QEMU_BIN="qemu-system-aarch64"
    QEMU_ARGS="-M virt -cpu cortex-a72 -m 512M"
fi

ISO_FILE="$OUTPUT_DIR/Cosmos.Tests.Kernel.iso"

echo -e "${YELLOW}========================================${NC}"
echo -e "${YELLOW}Cosmos Kernel Test Runner${NC}"
echo -e "${YELLOW}========================================${NC}"
echo "Architecture: $ARCH"
echo "Runtime: $RUNTIME_ID"
echo ""

# Step 1: Build the test kernel
echo -e "${YELLOW}[1/3] Building test kernel...${NC}"
export PATH="$PATH:$HOME/.dotnet/tools"
if ! dotnet publish -c Debug -r "$RUNTIME_ID" \
    -p:DefineConstants="$DEFINE_CONSTANTS" \
    "$PROJECT_FILE" \
    -o "$OUTPUT_DIR" \
    --no-restore > /dev/null 2>&1; then
    echo -e "${RED}✗ Build failed${NC}"
    dotnet publish -c Debug -r "$RUNTIME_ID" \
        -p:DefineConstants="$DEFINE_CONSTANTS" \
        "$PROJECT_FILE" \
        -o "$OUTPUT_DIR" \
        --no-restore
    exit 1
fi
echo -e "${GREEN}✓ Build successful${NC}"

# Step 2: Run in QEMU
echo -e "${YELLOW}[2/3] Running tests in QEMU...${NC}"
rm -f "$LOG_FILE"

# Start QEMU in background
$QEMU_BIN -cdrom "$ISO_FILE" $QEMU_ARGS \
    -serial file:"$LOG_FILE" \
    -nographic > /dev/null 2>&1 &
QEMU_PID=$!

# Wait for test completion or timeout
elapsed=0
while [ $elapsed -lt $TIMEOUT ]; do
    if [ -f "$LOG_FILE" ] && grep -q "TEST_END:" "$LOG_FILE" 2>/dev/null; then
        break
    fi
    sleep 1
    elapsed=$((elapsed + 1))
done

# Kill QEMU
kill $QEMU_PID 2>/dev/null || true
pkill -9 -f "$QEMU_BIN" 2>/dev/null || true

# Check if we got results
if [ ! -f "$LOG_FILE" ]; then
    echo -e "${RED}✗ No output from QEMU${NC}"
    exit 1
fi

if ! grep -q "TEST_END:" "$LOG_FILE" 2>/dev/null; then
    echo -e "${RED}✗ Tests did not complete (timeout after ${TIMEOUT}s)${NC}"
    echo ""
    echo "Last output:"
    tail -20 "$LOG_FILE" 2>/dev/null || echo "(no output)"
    exit 1
fi

echo -e "${GREEN}✓ Tests completed${NC}"

# Step 3: Parse results
echo -e "${YELLOW}[3/3] Parsing test results...${NC}"
echo ""

# Extract test results (between TEST_START and TEST_END)
if grep -q "TEST_START" "$LOG_FILE"; then
    # Show individual test results with proper pass/fail indication and categories
    # Two formats: "test_name: PASS (...)" OR "test_name: ...\nPASS (...)"
    prev_test=""
    failure_messages=""
    while IFS= read -r line; do
        if [[ "$line" =~ ^CATEGORY: ]]; then
            # Display category header
            category="${line#CATEGORY: }"
            echo ""
            echo -e "${YELLOW}${category}${NC}"
        elif [[ "$line" =~ ^\[ASSERT_FAIL: ]]; then
            # Collect assertion failure messages
            failure_msg="${line#\[ASSERT_FAIL: }"
            failure_msg="${failure_msg%\]}"
            if [[ -n "$failure_messages" ]]; then
                failure_messages="${failure_messages}"$'\n'"${failure_msg}"
            else
                failure_messages="${failure_msg}"
            fi
            continue
        elif [[ "$line" =~ ^\[ASSERT_ ]]; then
            # Skip other assertion debug lines
            continue
        elif [[ "$line" =~ ^test_.*:\ PASS\ \( ]]; then
            # Test name and PASS on same line (match ": PASS (")
            echo -e "  ${GREEN}✓${NC} ${line}"
            failure_messages=""
        elif [[ "$line" =~ ^test_.*:\ FAIL\ \( ]]; then
            # Test name and FAIL on same line (match ": FAIL (")
            echo -e "  ${RED}✗${NC} ${line}"
            # Show failure messages
            if [[ -n "$failure_messages" ]]; then
                while IFS= read -r msg; do
                    echo -e "    ${RED}→${NC} $msg"
                done <<< "$failure_messages"
            fi
            failure_messages=""
        elif [[ "$line" =~ ^test_ ]]; then
            # Test name only (result on next line)
            # Extract test name and check for ASSERT_FAIL on same line
            test_name=$(echo "$line" | sed 's/^\(test_[^:]*:\).*/\1/')
            prev_test="$test_name"
            # Check if there's an ASSERT_FAIL on the same line
            if [[ "$line" =~ \[ASSERT_FAIL:\ ([^\]]*)\] ]]; then
                failure_msg="${BASH_REMATCH[1]}"
                failure_messages="$failure_msg"
            else
                failure_messages=""
            fi
        elif [[ "$line" =~ ^PASS\ \( ]]; then
            # Result line only - PASS (test name was on previous line)
            echo -e "  ${GREEN}✓${NC} ${prev_test} ${line}"
            failure_messages=""
        elif [[ "$line" =~ ^FAIL\ \( ]]; then
            # Result line only - FAIL (test name was on previous line)
            echo -e "  ${RED}✗${NC} ${prev_test} ${line}"
            # Show failure messages
            if [[ -n "$failure_messages" ]]; then
                while IFS= read -r msg; do
                    echo -e "    ${RED}→${NC} $msg"
                done <<< "$failure_messages"
            fi
            failure_messages=""
        elif [[ "$line" =~ TEST_END ]]; then
            echo ""
            echo "$line"
        fi
    done < <(sed -n '/TEST_START/,/TEST_END:/p' "$LOG_FILE")
fi

echo ""

# Check final status
if grep -q "\[SUCCESS\]" "$LOG_FILE"; then
    echo -e "${GREEN}========================================${NC}"
    echo -e "${GREEN}✓ ALL TESTS PASSED${NC}"
    echo -e "${GREEN}========================================${NC}"
    exit 0
else
    echo -e "${RED}========================================${NC}"
    echo -e "${RED}✗ SOME TESTS FAILED${NC}"
    echo -e "${RED}========================================${NC}"
    exit 1
fi
