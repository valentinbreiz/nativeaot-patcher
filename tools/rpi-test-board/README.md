# Cosmos RPi Dev Board - Hardware CI/CD Testing

This directory contains firmware and tools for the Cosmos-RPi-Dev-Board PCB, which enables automated hardware testing of NativeAOT kernels on real Raspberry Pi 4B devices.

## Hardware Overview

The PCB (designed by Diamond Master) includes:

- **STM32H563RGT6**: Main controller
  - Ethernet (RMII) for TFTP server
  - SDMMC for MicroSD storage
  - UART for Raspberry Pi test output
  - SPI for ESP32 communication
  - GPIO for power control

- **ESP32-S3**: WiFi module
  - HTTP API for GitHub Actions
  - SPI slave to STM32

- **5 Status LEDs**: Visual feedback

## Directory Structure

```
rpi-test-board/
├── esp32/                  # ESP32-S3 firmware (ESP-IDF)
│   ├── main/
│   │   └── main.c         # Main application
│   ├── CMakeLists.txt
│   └── Kconfig.projbuild  # Configuration options
│
├── stm32/                  # STM32H563 firmware (placeholder)
│   └── ...
│
├── scripts/
│   └── rpi-test-controller.py  # Python controller for CI
│
└── docs/
    └── PROTOCOL.md        # Communication protocol specification
```

## Workflow

```
GitHub Actions ──► ESP32 (WiFi) ──► STM32 (SPI) ──► SD Card
                                                      │
                                                      ▼
                                                 TFTP Server
                                                      │
                                                      ▼
                                              Raspberry Pi 4B
                                                      │
                                                      ▼ (UART)
                                                   STM32
                                                      │
                                                      ▼ (SPI)
                                                   ESP32
                                                      │
                                                      ▼ (WiFi)
                                              GitHub Actions
```

## Building the ESP32 Firmware

1. Install ESP-IDF v5.0+:
   ```bash
   git clone --recursive https://github.com/espressif/esp-idf.git
   cd esp-idf
   ./install.sh
   source export.sh
   ```

2. Configure WiFi credentials:
   ```bash
   cd esp32
   idf.py menuconfig
   # Navigate to: Cosmos RPi Dev Board Configuration
   # Set WiFi SSID and Password
   ```

3. Build and flash:
   ```bash
   idf.py set-target esp32s3
   idf.py build
   idf.py flash
   ```

## Using the Python Controller

The `rpi-test-controller.py` script communicates with the PCB from a self-hosted GitHub Actions runner.

### HTTP Mode (PCB on network)

```bash
python scripts/rpi-test-controller.py \
    --mode http \
    --endpoint http://192.168.1.100:8080 \
    --iso kernel.iso \
    --output uart.log \
    --timeout 180
```

### Serial Mode (direct connection)

```bash
python scripts/rpi-test-controller.py \
    --mode serial \
    --port /dev/ttyUSB0 \
    --baud 115200 \
    --iso kernel.iso \
    --output uart.log \
    --timeout 180
```

## GitHub Actions Integration

The workflow `.github/workflows/hardware-tests-rpi4.yml` runs on self-hosted runners labeled `rpi4-test-board`.

### Setting Up a Self-Hosted Runner

1. On your runner machine (with PCB connected):
   ```bash
   # Download GitHub Actions runner
   # Configure with: ./config.sh --url <repo> --token <token>
   # Add labels: rpi4-test-board
   ```

2. Set repository secrets:
   - `RPI_TEST_BOARD_ENDPOINT`: HTTP endpoint (e.g., `http://192.168.1.100:8080`)
   - OR `RPI_TEST_BOARD_SERIAL`: Serial port (e.g., `/dev/ttyUSB0`)

3. Trigger tests:
   - Add `test-on-hardware` label to a PR
   - Or use `workflow_dispatch` to run manually

## Protocol Documentation

See [docs/PROTOCOL.md](docs/PROTOCOL.md) for:
- HTTP API endpoints
- SPI command format
- UART test protocol
- LED status codes
- Error codes

## Test Output Format

The Raspberry Pi kernel sends test results via UART using the same binary protocol as QEMU tests:

```
[CMD:1][LEN:2][PAYLOAD:N]

Commands:
  100: TestSuiteStart (expected_tests + suite_name)
  101: TestStart (test_number + test_name)
  102: TestPass (test_number + duration_ms)
  103: TestFail (test_number + error_message)
  104: TestSkip (test_number + skip_reason)
  105: TestSuiteEnd (total + passed + failed)

End marker: 0xDE 0xAD 0xBE 0xEF 0xCA 0xFE 0xBA 0xBE
```

## Status LEDs

| LED | Color  | Meaning                    |
|-----|--------|----------------------------|
| 1   | Green  | Power on                   |
| 2   | Blue   | WiFi connected             |
| 3   | Yellow | Job in progress            |
| 4   | White  | Test running               |
| 5   | R/G    | Result (red=fail, green=pass) |

## Troubleshooting

### ESP32 not connecting to WiFi
- Check credentials in `menuconfig`
- Verify network is 2.4GHz (ESP32 doesn't support 5GHz)

### Upload fails
- Check SD card is inserted and FAT32 formatted
- Verify STM32 is powered and SPI communication works

### RPi doesn't boot
- Check Ethernet cable between STM32 and RPi
- Verify TFTP server started (LED 3 should be on)
- Check RPi is configured for network boot

### No UART output
- Verify RPi GPIO14/15 connected to STM32 UART
- Check baud rate (115200)
- Ensure kernel sends to UART (Serial.WriteString)
