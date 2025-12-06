# Cosmos-RPi-Dev-Board PCB Emulation

This directory contains the Renode emulation setup for testing the Cosmos-RPi-Dev-Board PCB before physical hardware is available.

## Overview

The emulation validates:
- STM32H563 firmware (SPI, SDMMC, UART, GPIO)
- ESP32-S3 firmware (WiFi, SPI, HTTP client)
- SPI communication between MCUs
- UART test protocol parsing
- Full boot flow with kernel ISO

## Directory Structure

```
emulation/
├── platforms/                  # Renode platform definitions
│   ├── stm32h563.repl         # STM32H563RGT6 (main controller)
│   └── esp32s3.repl           # ESP32-S3 (WiFi module)
├── scripts/
│   ├── cosmos-rpi-devboard.resc  # Main emulation script
│   └── run-pcb-emulation.py      # Python test harness
├── tests/
│   └── test_pcb_emulation.robot  # Robot Framework tests
└── README.md
```

## Prerequisites

### Install Renode

```bash
# Linux (portable version - recommended, no GTK/Mono dependencies)
wget https://github.com/renode/renode/releases/download/v1.16.0/renode-1.16.0.linux-portable.tar.gz
tar -xzf renode-1.16.0.linux-portable.tar.gz
sudo mv renode_1.16.0_portable /opt/renode
sudo ln -sf /opt/renode/renode /usr/local/bin/renode

# macOS
brew install renode

# Or from source: https://renode.io/
```

### Install QEMU (for RPi kernel emulation)

```bash
sudo apt install qemu-system-aarch64 qemu-efi-aarch64
```

## Usage

### Interactive Emulation

```bash
cd tools/rpi-test-board/emulation

# Start Renode with PCB emulation (headless mode)
renode --disable-xwt --console scripts/cosmos-rpi-devboard.resc

# In Renode console:
(monitor) start    # Start emulation
(monitor) pause    # Pause
(monitor) quit     # Exit
```

### With Kernel ISO

```bash
# Set ISO path before loading script
renode -e "\$iso_path = '/path/to/kernel.iso'; include @scripts/cosmos-rpi-devboard.resc; start"
```

### Python Test Harness

```bash
# Run simplified emulation (QEMU only)
python scripts/run-pcb-emulation.py \
    --iso /path/to/kernel.iso \
    --output uart.log \
    --timeout 120

# With firmware binaries (when available)
python scripts/run-pcb-emulation.py \
    --iso /path/to/kernel.iso \
    --esp32-firmware esp32.elf \
    --stm32-firmware stm32.elf \
    --output uart.log
```

### Robot Framework Tests

```bash
pip install robotframework

cd tools/rpi-test-board/emulation
robot --outputdir results tests/
```

## Emulation Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    Renode Emulation                          │
│                                                              │
│  ┌──────────────┐   SPI Bus    ┌──────────────┐             │
│  │  ESP32-S3    │◄────────────►│  STM32H563   │             │
│  │              │              │              │             │
│  │  WiFi (mock) │              │  SDMMC ──────┼──► disk.img │
│  │  HTTP client │              │  UART1 ──────┼──┐          │
│  │  SPI master  │              │  GPIO        │  │          │
│  └──────────────┘              └──────────────┘  │          │
│                                                   │          │
│  ┌───────────────────────────────────────────────┼────────┐ │
│  │                 Virtual UART Bridge            │        │ │
│  └───────────────────────────────────────────────┼────────┘ │
│                                                   │          │
│                                                   ▼          │
│                                      ┌──────────────────┐   │
│         kernel.iso ─────────────────►│   QEMU ARM64     │   │
│         (from artifact)              │   (RPi kernel)   │   │
│                                      └──────────────────┘   │
│                                                              │
└─────────────────────────────────────────────────────────────┘
```

## What's Emulated

Based on KiCad schematic `CosmosRpiDevBoard.kicad_sch` and STM32CubeMX config `MainController.ioc`:

| Component | Emulation Level | Pin Configuration |
|-----------|-----------------|-------------------|
| STM32H563 CPU | Full (Cortex-M33) | Renode |
| STM32 USART1 | Full | PA9(TX), PA10(RX) → RPi UART |
| STM32 USART6 | Full | PC6(TX), PC7(RX) → Debug |
| STM32 SPI1 (slave) | Full | PA3(NSS), PA6(MISO), PB3(SCK), PB5(MOSI) |
| STM32 SPI2 (slave) | Full | PA4(NSS), PB13(SCK), PC2(MISO), PC3(MOSI) |
| STM32 SDMMC1 | Partial | PC8-12, PD2 (4-bit wide) |
| STM32 GPIO | Full | PB6(LED), PB7(RST), PB8(BOOT) |
| STM32 Ethernet | Stub | RMII interface (PA1/2/5/7, PB12/15, PC1/4/5) |
| ESP32-S3 CPU | Full (Xtensa LX7) | Renode |
| ESP32 UART | Stub | GPIO17(TX), GPIO18(RX) |
| ESP32 SPI1 (master) | Stub | GPIO9(CS), GPIO10(MOSI), GPIO11(CLK), GPIO12(MISO) |
| ESP32 SPI2 (master) | Stub | GPIO5(CS), GPIO6(MOSI), GPIO7(CLK), GPIO8(MISO) |
| ESP32 WiFi | External | ISO injection via file |
| ESP32 USB | Stub | GPIO19(D-), GPIO20(D+) |
| RPi 4B | QEMU | ARM64 kernel boot |

## What's NOT Emulated

- Ethernet PHY timing (RMII)
- Real WiFi RF/network
- Physical SD card performance
- Power sequencing delays
- LED visual feedback
- Thermal behavior

These require physical PCB testing.

## CI Integration

The emulation runs in GitHub Actions:
- **pcb-emulation-tests.yml** - Validates platform files and runs emulation
- Triggered on changes to `tools/rpi-test-board/**`
- Full emulation runs on `main` branch or manual trigger

## Debugging

### View UART Output

```bash
# In Renode
(cosmos-rpi-devboard) mach set "stm32"
(cosmos-rpi-devboard) showAnalyzer usart1
```

### Check SPI Traffic

```bash
# Log SPI transactions
(cosmos-rpi-devboard) sysbus LogPeripheralAccess spi1 true
```

### Inspect Memory

```bash
(cosmos-rpi-devboard) sysbus ReadDoubleWord 0x20000000
```

## Adding Firmware

When STM32/ESP32 firmware is ready:

1. Build firmware as ELF file
2. Load in emulation:
   ```
   renode -e "$stm32_firmware = 'path/to/stm32.elf'; include @scripts/cosmos-rpi-devboard.resc"
   ```

## Troubleshooting

### "Platform not found"

Ensure you're in the `emulation/` directory or use absolute paths:
```bash
renode -e "path add '/full/path/to/emulation'; include @scripts/cosmos-rpi-devboard.resc"
```

### "ESP32 not supported"

ESP32 support in Renode is community-contributed. Some features may be incomplete.
Use the simplified QEMU-only mode for kernel testing.

### UART log empty

Check that the kernel actually outputs to UART. The binary protocol requires the
kernel to send `[CMD:1][LEN:2][PAYLOAD:N]` format messages.
