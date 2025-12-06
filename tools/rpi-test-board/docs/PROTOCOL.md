# Cosmos RPi Dev Board Communication Protocol

This document describes the communication protocols used by the Cosmos-RPi-Dev-Board PCB for hardware CI/CD testing.

## System Overview

```
┌─────────────────┐     WiFi/HTTP      ┌─────────────────┐
│  GitHub Actions │◄──────────────────►│    ESP32-S3     │
│  (Cloud)        │                    │  (WiFi Module)  │
└─────────────────┘                    └────────┬────────┘
                                                │ SPI
                                                ▼
┌─────────────────┐     Ethernet       ┌─────────────────┐
│  Raspberry Pi   │◄──────────────────►│   STM32H563     │
│  4B (DUT)       │     (TFTP Boot)    │ (Main Controller)│
│                 │◄──────────────────►│                 │
│                 │     UART (Tests)   │                 │
└─────────────────┘                    └─────────────────┘
```

## 1. ESP32 HTTP API (GitHub Actions → ESP32)

The ESP32-S3 exposes a REST API for receiving commands from GitHub Actions.

### Endpoints

#### GET /status
Get current board status.

**Response:**
```json
{
  "state": "idle|uploading|flashing|booting|running|completed|error",
  "message": "Human readable status",
  "progress": 0-100,
  "uptime_ms": 12345,
  "wifi_rssi": -50
}
```

#### POST /upload
Upload kernel ISO for testing.

**Request:** `multipart/form-data` with `iso` file field

**Response:**
```json
{
  "success": true,
  "size": 1234567,
  "checksum": "sha256:..."
}
```

#### POST /run
Start test execution.

**Response:**
```json
{
  "success": true,
  "job_id": "abc123"
}
```

#### GET /uart-log
Get collected UART output from Raspberry Pi.

**Response:** Raw text/binary UART log

#### POST /reset
Reset board to idle state.

**Response:**
```json
{
  "success": true
}
```

## 2. SPI Protocol (ESP32 ↔ STM32)

The ESP32 acts as SPI master, STM32 as SPI slave.

### Message Format

```
┌──────────┬──────────┬──────────────┐
│ CMD (1B) │ LEN (4B) │ DATA (N B)   │
└──────────┴──────────┴──────────────┘
```

- **CMD**: Command byte
- **LEN**: Little-endian 32-bit length of DATA
- **DATA**: Variable length payload

### Commands (ESP32 → STM32)

| CMD  | Name        | Description                          | Payload              |
|------|-------------|--------------------------------------|----------------------|
| 0x01 | PING        | Check if STM32 is ready              | None                 |
| 0x02 | UPLOAD_START| Begin ISO upload, specify size       | u32 total_size       |
| 0x03 | UPLOAD_DATA | Send ISO chunk                       | raw bytes            |
| 0x04 | UPLOAD_END  | Finish upload, verify checksum       | u8[32] sha256        |
| 0x05 | RUN_TEST    | Start TFTP + boot RPi + collect UART | None                 |
| 0x06 | GET_STATUS  | Get current status                   | None                 |
| 0x07 | GET_LOG     | Get UART log                         | u32 offset, u32 len  |
| 0x08 | RESET       | Reset to idle state                  | None                 |

### Responses (STM32 → ESP32)

| RSP  | Name        | Description                          | Payload              |
|------|-------------|--------------------------------------|----------------------|
| 0x10 | OK          | Command successful                   | None or cmd-specific |
| 0x11 | ERROR       | Command failed                       | u8 error_code, string|
| 0x12 | BUSY        | Board is busy                        | u8 current_state     |
| 0x13 | DATA        | Data response                        | variable             |
| 0x14 | STATUS      | Status response                      | see below            |

### Status Payload Format

```
┌──────────┬──────────┬────────────────┐
│ STATE(1B)│ PROG(1B) │ MESSAGE (N B)  │
└──────────┴──────────┴────────────────┘
```

State values:
- 0x00: IDLE
- 0x01: UPLOADING
- 0x02: FLASHING (writing to SD)
- 0x03: BOOTING (RPi booting via TFTP)
- 0x04: RUNNING (tests executing)
- 0x05: COMPLETED
- 0xFF: ERROR

## 3. UART Protocol (Raspberry Pi → STM32)

This uses the existing Cosmos TestRunner binary protocol.

### Message Format

```
┌──────────┬──────────┬──────────────┐
│ CMD (1B) │ LEN (2B) │ PAYLOAD (N)  │
└──────────┴──────────┴──────────────┘
```

- **CMD**: Message type (see below)
- **LEN**: Little-endian 16-bit length
- **PAYLOAD**: Message-specific data

### Message Types

| CMD  | Name          | Payload Format                           |
|------|---------------|------------------------------------------|
| 100  | TestSuiteStart| u16 expected_tests + string suite_name   |
| 101  | TestStart     | u16 test_number + string test_name       |
| 102  | TestPass      | u16 test_number + u32 duration_ms        |
| 103  | TestFail      | u16 test_number + string error_message   |
| 104  | TestSkip      | u16 test_number + string skip_reason     |
| 105  | TestSuiteEnd  | u16 total + u16 passed + u16 failed      |

### End Marker

After TestSuiteEnd, the kernel sends an 8-byte marker to signal completion:

```
0xDE 0xAD 0xBE 0xEF 0xCA 0xFE 0xBA 0xBE
```

The STM32 monitors for this marker to know when to stop collecting UART data.

## 4. TFTP Boot (STM32 → Raspberry Pi)

The STM32 implements a minimal TFTP server for PXE booting the Raspberry Pi.

### Boot Files

The STM32 serves these files from the MicroSD card:

1. `bootcode.bin` - RPi bootloader (pre-installed on SD)
2. `start4.elf` - GPU firmware (pre-installed on SD)
3. `kernel8.img` - ARM64 kernel (extracted from ISO)
4. `config.txt` - Boot configuration
5. `cmdline.txt` - Kernel command line

### config.txt

```
arm_64bit=1
enable_uart=1
uart_2ndstage=1
kernel=kernel8.img
```

### Network Configuration

- STM32 Ethernet: Static IP 192.168.42.1
- RPi DHCP: Gets IP from STM32 (192.168.42.2)
- TFTP Server: Port 69 on 192.168.42.1

## 5. LED Status Indicators

The PCB has 5 status LEDs:

| LED | Color  | Meaning                              |
|-----|--------|--------------------------------------|
| 1   | Green  | Power on                             |
| 2   | Blue   | WiFi connected                       |
| 3   | Yellow | Job in progress (SPI activity)       |
| 4   | White  | Test running (UART activity)         |
| 5   | Red/Grn| Test result (red=fail, green=pass)   |

## 6. Error Codes

| Code | Description                          |
|------|--------------------------------------|
| 0x01 | SD card not found                    |
| 0x02 | SD card write failed                 |
| 0x03 | ISO checksum mismatch                |
| 0x04 | TFTP server failed to start          |
| 0x05 | RPi failed to boot (no DHCP request) |
| 0x06 | RPi failed to boot (no TFTP request) |
| 0x07 | Test timeout (no end marker)         |
| 0x08 | SPI communication error              |
| 0x09 | WiFi disconnected                    |

## 7. Timing Considerations

| Operation              | Typical Time  | Timeout     |
|------------------------|---------------|-------------|
| ISO Upload (10MB)      | 5-10 seconds  | 60 seconds  |
| SD Card Write          | 2-5 seconds   | 30 seconds  |
| TFTP Boot              | 10-15 seconds | 60 seconds  |
| Test Execution         | varies        | configurable|
| Total (typical)        | 30-60 seconds | 180 seconds |
