#!/usr/bin/env python3
"""
Raspberry Pi Test Board Controller

This script runs on the self-hosted runner machine and communicates with the
Cosmos-RPi-Dev-Board PCB to run kernel tests on real Raspberry Pi 4B hardware.

Communication modes:
1. HTTP API mode: PCB exposes REST API via ESP32 WiFi
2. Serial mode: Direct connection to STM32 via USB-UART

Usage:
    # HTTP mode (PCB on network)
    python rpi-test-controller.py --mode http --endpoint http://192.168.1.100:8080 \
        --iso kernel.iso --output uart.log --timeout 120

    # Serial mode (direct connection)
    python rpi-test-controller.py --mode serial --port /dev/ttyUSB0 --baud 115200 \
        --iso kernel.iso --output uart.log --timeout 120

Protocol (Serial mode):
    Commands sent to PCB:
        CMD_PING     = 0x01  # Check if board is ready
        CMD_UPLOAD   = 0x02  # Upload ISO (followed by size + data)
        CMD_RUN      = 0x03  # Start test execution
        CMD_STATUS   = 0x04  # Get current status
        CMD_GET_LOG  = 0x05  # Get UART log from RPi
        CMD_RESET    = 0x06  # Reset board state

    Responses from PCB:
        RSP_OK       = 0x10  # Command successful
        RSP_ERROR    = 0x11  # Command failed
        RSP_BUSY     = 0x12  # Board is busy
        RSP_DATA     = 0x13  # Data follows (size + data)
        RSP_STATUS   = 0x14  # Status info follows

    Status values:
        STATUS_IDLE       = 0x00
        STATUS_UPLOADING  = 0x01
        STATUS_FLASHING   = 0x02
        STATUS_BOOTING    = 0x03
        STATUS_RUNNING    = 0x04
        STATUS_COMPLETED  = 0x05
        STATUS_ERROR      = 0xFF
"""

import argparse
import json
import os
import struct
import sys
import time
from pathlib import Path

# Command bytes
CMD_PING = 0x01
CMD_UPLOAD = 0x02
CMD_RUN = 0x03
CMD_STATUS = 0x04
CMD_GET_LOG = 0x05
CMD_RESET = 0x06

# Response bytes
RSP_OK = 0x10
RSP_ERROR = 0x11
RSP_BUSY = 0x12
RSP_DATA = 0x13
RSP_STATUS = 0x14

# Status values
STATUS_IDLE = 0x00
STATUS_UPLOADING = 0x01
STATUS_FLASHING = 0x02
STATUS_BOOTING = 0x03
STATUS_RUNNING = 0x04
STATUS_COMPLETED = 0x05
STATUS_ERROR = 0xFF

STATUS_NAMES = {
    STATUS_IDLE: "idle",
    STATUS_UPLOADING: "uploading",
    STATUS_FLASHING: "flashing",
    STATUS_BOOTING: "booting",
    STATUS_RUNNING: "running",
    STATUS_COMPLETED: "completed",
    STATUS_ERROR: "error"
}

# Test end marker (same as kernel protocol)
TEST_END_MARKER = bytes([0xDE, 0xAD, 0xBE, 0xEF, 0xCA, 0xFE, 0xBA, 0xBE])


class HttpController:
    """Controller for HTTP API mode"""

    def __init__(self, endpoint: str, timeout: int):
        import requests
        self.endpoint = endpoint.rstrip('/')
        self.timeout = timeout
        self.session = requests.Session()

    def ping(self) -> bool:
        try:
            r = self.session.get(f"{self.endpoint}/status", timeout=5)
            return r.status_code == 200
        except Exception:
            return False

    def upload_iso(self, iso_path: str) -> bool:
        print(f"[HTTP] Uploading ISO: {iso_path}")
        with open(iso_path, 'rb') as f:
            files = {'iso': (os.path.basename(iso_path), f, 'application/octet-stream')}
            r = self.session.post(f"{self.endpoint}/upload", files=files, timeout=300)
            if r.status_code != 200:
                print(f"[HTTP] Upload failed: {r.text}")
                return False
        print("[HTTP] Upload complete")
        return True

    def run_test(self) -> bool:
        print("[HTTP] Starting test execution...")
        r = self.session.post(f"{self.endpoint}/run", timeout=10)
        return r.status_code == 200

    def get_status(self) -> tuple[str, str]:
        try:
            r = self.session.get(f"{self.endpoint}/status", timeout=5)
            if r.status_code == 200:
                data = r.json()
                return data.get('state', 'unknown'), data.get('message', '')
        except Exception:
            pass
        return 'unknown', ''

    def get_uart_log(self) -> str:
        try:
            r = self.session.get(f"{self.endpoint}/uart-log", timeout=30)
            if r.status_code == 200:
                return r.text
        except Exception:
            pass
        return ''

    def wait_for_completion(self) -> tuple[bool, str]:
        """Wait for test to complete, return (success, uart_log)"""
        start_time = time.time()
        while time.time() - start_time < self.timeout:
            status, message = self.get_status()
            print(f"[HTTP] Status: {status} - {message}")

            if status == 'completed':
                return True, self.get_uart_log()
            elif status == 'error':
                return False, self.get_uart_log()

            time.sleep(1)

        print("[HTTP] Timeout waiting for completion")
        return False, self.get_uart_log()


class SerialController:
    """Controller for serial mode (direct STM32 connection)"""

    def __init__(self, port: str, baud: int, timeout: int):
        import serial
        self.port = port
        self.baud = baud
        self.timeout = timeout
        self.serial = None

    def connect(self) -> bool:
        import serial
        try:
            self.serial = serial.Serial(self.port, self.baud, timeout=1)
            time.sleep(0.1)  # Wait for connection to stabilize
            return True
        except Exception as e:
            print(f"[Serial] Failed to connect: {e}")
            return False

    def close(self):
        if self.serial:
            self.serial.close()

    def send_command(self, cmd: int, data: bytes = b'') -> tuple[int, bytes]:
        """Send command and receive response"""
        if not self.serial:
            return RSP_ERROR, b''

        # Send command: [CMD:1][LEN:4][DATA:N]
        packet = bytes([cmd]) + struct.pack('<I', len(data)) + data
        self.serial.write(packet)

        # Read response: [RSP:1][LEN:4][DATA:N]
        rsp = self.serial.read(1)
        if not rsp:
            return RSP_ERROR, b''

        rsp_code = rsp[0]
        len_bytes = self.serial.read(4)
        if len(len_bytes) < 4:
            return rsp_code, b''

        data_len = struct.unpack('<I', len_bytes)[0]
        rsp_data = self.serial.read(data_len) if data_len > 0 else b''

        return rsp_code, rsp_data

    def ping(self) -> bool:
        if not self.connect():
            return False
        rsp, _ = self.send_command(CMD_PING)
        return rsp == RSP_OK

    def upload_iso(self, iso_path: str) -> bool:
        print(f"[Serial] Uploading ISO: {iso_path}")
        with open(iso_path, 'rb') as f:
            data = f.read()

        # Send in chunks (64KB each)
        chunk_size = 64 * 1024
        total_chunks = (len(data) + chunk_size - 1) // chunk_size

        # Start upload with total size
        rsp, _ = self.send_command(CMD_UPLOAD, struct.pack('<I', len(data)))
        if rsp != RSP_OK:
            print("[Serial] Failed to start upload")
            return False

        for i in range(total_chunks):
            chunk = data[i * chunk_size:(i + 1) * chunk_size]
            # Send chunk (command 0x02 with chunk data)
            self.serial.write(chunk)
            # Wait for ACK
            ack = self.serial.read(1)
            if not ack or ack[0] != RSP_OK:
                print(f"[Serial] Chunk {i+1}/{total_chunks} failed")
                return False
            print(f"[Serial] Chunk {i+1}/{total_chunks} sent")

        print("[Serial] Upload complete")
        return True

    def run_test(self) -> bool:
        print("[Serial] Starting test execution...")
        rsp, _ = self.send_command(CMD_RUN)
        return rsp == RSP_OK

    def get_status(self) -> tuple[str, str]:
        rsp, data = self.send_command(CMD_STATUS)
        if rsp == RSP_STATUS and len(data) >= 1:
            status_code = data[0]
            message = data[1:].decode('utf-8', errors='ignore') if len(data) > 1 else ''
            return STATUS_NAMES.get(status_code, 'unknown'), message
        return 'unknown', ''

    def get_uart_log(self) -> str:
        rsp, data = self.send_command(CMD_GET_LOG)
        if rsp == RSP_DATA:
            return data.decode('utf-8', errors='ignore')
        return ''

    def wait_for_completion(self) -> tuple[bool, str]:
        """Wait for test to complete, return (success, uart_log)"""
        start_time = time.time()
        while time.time() - start_time < self.timeout:
            status, message = self.get_status()
            print(f"[Serial] Status: {status} - {message}")

            if status == 'completed':
                self.close()
                return True, self.get_uart_log()
            elif status == 'error':
                self.close()
                return False, self.get_uart_log()

            time.sleep(1)

        print("[Serial] Timeout waiting for completion")
        self.close()
        return False, self.get_uart_log()


def main():
    parser = argparse.ArgumentParser(description='Raspberry Pi Test Board Controller')
    parser.add_argument('--mode', choices=['http', 'serial'], required=True,
                       help='Communication mode')
    parser.add_argument('--endpoint', type=str,
                       help='HTTP endpoint (for http mode)')
    parser.add_argument('--port', type=str,
                       help='Serial port (for serial mode)')
    parser.add_argument('--baud', type=int, default=115200,
                       help='Baud rate (for serial mode)')
    parser.add_argument('--iso', type=str, required=True,
                       help='Path to kernel ISO')
    parser.add_argument('--output', type=str, required=True,
                       help='Path to write UART log')
    parser.add_argument('--timeout', type=int, default=120,
                       help='Timeout in seconds')

    args = parser.parse_args()

    # Validate arguments
    if args.mode == 'http' and not args.endpoint:
        print("Error: --endpoint required for http mode")
        sys.exit(1)
    if args.mode == 'serial' and not args.port:
        print("Error: --port required for serial mode")
        sys.exit(1)
    if not os.path.exists(args.iso):
        print(f"Error: ISO file not found: {args.iso}")
        sys.exit(1)

    # Create controller
    if args.mode == 'http':
        controller = HttpController(args.endpoint, args.timeout)
    else:
        controller = SerialController(args.port, args.baud, args.timeout)

    # Check connectivity
    print(f"[Controller] Checking board connectivity...")
    if not controller.ping():
        print("Error: Cannot connect to test board")
        sys.exit(1)
    print("[Controller] Board is online")

    # Upload ISO
    if not controller.upload_iso(args.iso):
        print("Error: Failed to upload ISO")
        sys.exit(1)

    # Start test
    if not controller.run_test():
        print("Error: Failed to start test")
        sys.exit(1)

    # Wait for completion
    success, uart_log = controller.wait_for_completion()

    # Save UART log
    output_dir = os.path.dirname(args.output)
    if output_dir and not os.path.exists(output_dir):
        os.makedirs(output_dir)
    with open(args.output, 'w') as f:
        f.write(uart_log)
    print(f"[Controller] UART log saved to: {args.output}")

    # Check for test end marker
    if TEST_END_MARKER in uart_log.encode('utf-8', errors='ignore'):
        print("[Controller] Test suite completed successfully")
    elif success:
        print("[Controller] Test completed but end marker not found")
    else:
        print("[Controller] Test failed or timed out")

    sys.exit(0 if success else 1)


if __name__ == '__main__':
    main()
