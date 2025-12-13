#!/usr/bin/env python3
"""
Cosmos-RPi-Dev-Board PCB Emulation Runner

This script orchestrates the full PCB emulation:
1. Starts Renode with ESP32-S3 + STM32H563 emulation
2. Injects the kernel ISO (from GitHub Actions artifact)
3. Starts QEMU ARM64 to emulate the Raspberry Pi
4. Connects STM32 UART to QEMU serial
5. Captures test results
6. Outputs results in CI-compatible format

Usage:
    python run-pcb-emulation.py --iso kernel.iso --output uart.log --timeout 120

For CI:
    python run-pcb-emulation.py \
        --iso ${{ github.workspace }}/output-arm64/Kernel.iso \
        --esp32-firmware esp32-firmware.elf \
        --stm32-firmware stm32-firmware.elf \
        --output uart-output.log \
        --timeout 180 \
        --ci
"""

import argparse
import os
import subprocess
import sys
import time
import socket
import threading
import signal
from pathlib import Path
from typing import Optional, Tuple

# Test protocol constants
END_MARKER = bytes([0xDE, 0xAD, 0xBE, 0xEF, 0xCA, 0xFE, 0xBA, 0xBE])

# Test command codes
CMD_TEST_SUITE_START = 100
CMD_TEST_START = 101
CMD_TEST_PASS = 102
CMD_TEST_FAIL = 103
CMD_TEST_SKIP = 104
CMD_TEST_SUITE_END = 105


class PCBEmulator:
    """Orchestrates the full PCB emulation stack."""

    def __init__(
        self,
        iso_path: str,
        esp32_firmware: Optional[str] = None,
        stm32_firmware: Optional[str] = None,
        output_path: str = "uart-output.log",
        timeout: int = 120,
        ci_mode: bool = False,
    ):
        self.iso_path = Path(iso_path).resolve()
        self.esp32_firmware = Path(esp32_firmware).resolve() if esp32_firmware else None
        self.stm32_firmware = Path(stm32_firmware).resolve() if stm32_firmware else None
        self.output_path = Path(output_path).resolve()
        self.timeout = timeout
        self.ci_mode = ci_mode

        self.renode_process: Optional[subprocess.Popen] = None
        self.qemu_process: Optional[subprocess.Popen] = None
        self.uart_data = bytearray()
        self.test_complete = threading.Event()
        self.success = False

        # Find script directory for platform files
        self.script_dir = Path(__file__).parent
        self.emulation_dir = self.script_dir.parent
        self.platforms_dir = self.emulation_dir / "platforms"

    def validate_inputs(self) -> bool:
        """Validate all input files exist."""
        if not self.iso_path.exists():
            print(f"❌ ISO file not found: {self.iso_path}")
            return False

        if self.esp32_firmware and not self.esp32_firmware.exists():
            print(f"❌ ESP32 firmware not found: {self.esp32_firmware}")
            return False

        if self.stm32_firmware and not self.stm32_firmware.exists():
            print(f"❌ STM32 firmware not found: {self.stm32_firmware}")
            return False

        if not self.platforms_dir.exists():
            print(f"❌ Platforms directory not found: {self.platforms_dir}")
            return False

        return True

    def start_renode(self) -> bool:
        """Start Renode with the PCB emulation."""
        print("🚀 Starting Renode emulation...")

        # Build Renode command
        resc_path = self.script_dir / "cosmos-rpi-devboard.resc"

        renode_cmd = [
            "renode",
            "--disable-xwt",  # Headless mode
            "-e", f"$iso_path = '{self.iso_path}'",
            "-e", f"$uart_log_path = '{self.output_path}'",
            "-e", f"$timeout_seconds = {self.timeout}",
        ]

        if self.esp32_firmware:
            renode_cmd.extend(["-e", f"$esp32_firmware = '{self.esp32_firmware}'"])

        if self.stm32_firmware:
            renode_cmd.extend(["-e", f"$stm32_firmware = '{self.stm32_firmware}'"])

        # Include the main script and start
        renode_cmd.extend([
            "-e", f"path add '{self.emulation_dir}'",
            "-e", f"include @{resc_path}",
            "-e", "start",
        ])

        try:
            self.renode_process = subprocess.Popen(
                renode_cmd,
                stdout=subprocess.PIPE if self.ci_mode else None,
                stderr=subprocess.PIPE if self.ci_mode else None,
            )
            print(f"   Renode started (PID: {self.renode_process.pid})")
            return True
        except FileNotFoundError:
            print("❌ Renode not found. Please install Renode:")
            print("   https://renode.io/")
            return False
        except Exception as e:
            print(f"❌ Failed to start Renode: {e}")
            return False

    def start_qemu(self) -> bool:
        """Start QEMU ARM64 to emulate the Raspberry Pi."""
        print("🚀 Starting QEMU ARM64 (Raspberry Pi emulation)...")

        # Check for UEFI firmware
        uefi_paths = [
            "/usr/share/qemu-efi-aarch64/QEMU_EFI.fd",
            "/usr/share/AAVMF/AAVMF_CODE.fd",
            "/usr/share/edk2/aarch64/QEMU_EFI.fd",
        ]

        uefi_path = None
        for path in uefi_paths:
            if os.path.exists(path):
                uefi_path = path
                break

        if not uefi_path:
            print("⚠️  UEFI firmware not found, QEMU may not boot correctly")
            print("   Install: apt install qemu-efi-aarch64")

        qemu_cmd = [
            "qemu-system-aarch64",
            "-M", "virt",
            "-cpu", "cortex-a72",
            "-m", "512M",
            "-nographic",
            "-cdrom", str(self.iso_path),
            "-serial", "stdio",  # Connect to stdio for UART capture
        ]

        if uefi_path:
            qemu_cmd.extend(["-bios", uefi_path])

        try:
            self.qemu_process = subprocess.Popen(
                qemu_cmd,
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
            )
            print(f"   QEMU started (PID: {self.qemu_process.pid})")
            return True
        except FileNotFoundError:
            print("❌ QEMU not found. Please install QEMU:")
            print("   apt install qemu-system-aarch64")
            return False
        except Exception as e:
            print(f"❌ Failed to start QEMU: {e}")
            return False

    def capture_uart(self):
        """Capture UART output from QEMU and check for test completion."""
        if not self.qemu_process:
            return

        print("📡 Capturing UART output...")

        try:
            while not self.test_complete.is_set():
                # Read from QEMU stdout
                data = self.qemu_process.stdout.read(1)
                if not data:
                    break

                self.uart_data.extend(data)

                # Check for end marker
                if self.uart_data.endswith(END_MARKER):
                    print("✅ Test completion marker detected!")
                    self.test_complete.set()
                    break

        except Exception as e:
            print(f"⚠️  UART capture error: {e}")

    def wait_for_completion(self) -> bool:
        """Wait for test completion or timeout."""
        print(f"⏳ Waiting for test completion (timeout: {self.timeout}s)...")

        # Start UART capture thread
        capture_thread = threading.Thread(target=self.capture_uart, daemon=True)
        capture_thread.start()

        # Wait for completion or timeout
        completed = self.test_complete.wait(timeout=self.timeout)

        if completed:
            print("✅ Tests completed!")
            self.success = True
        else:
            print("❌ Test timeout reached!")
            self.success = False

        return completed

    def parse_results(self) -> dict:
        """Parse the UART log for test results."""
        results = {
            "suite_name": "",
            "total": 0,
            "passed": 0,
            "failed": 0,
            "skipped": 0,
            "tests": [],
        }

        # Write raw data to output file
        with open(self.output_path, "wb") as f:
            f.write(self.uart_data)

        # Parse binary protocol
        data = self.uart_data
        offset = 0

        while offset < len(data) - 3:  # Need at least cmd + len(2)
            cmd = data[offset]
            if cmd < 100 or cmd > 105:
                offset += 1
                continue

            length = (data[offset + 1] << 8) | data[offset + 2]
            if offset + 3 + length > len(data):
                break

            payload = data[offset + 3:offset + 3 + length]

            if cmd == CMD_TEST_SUITE_START:
                expected = (payload[0] << 8) | payload[1] if len(payload) >= 2 else 0
                suite_name = payload[2:].decode("utf-8", errors="replace") if len(payload) > 2 else ""
                results["suite_name"] = suite_name
                results["total"] = expected
                print(f"   Suite: {suite_name} ({expected} tests)")

            elif cmd == CMD_TEST_START:
                test_num = (payload[0] << 8) | payload[1] if len(payload) >= 2 else 0
                test_name = payload[2:].decode("utf-8", errors="replace") if len(payload) > 2 else ""
                print(f"   Test {test_num}: {test_name}...", end=" ")

            elif cmd == CMD_TEST_PASS:
                test_num = (payload[0] << 8) | payload[1] if len(payload) >= 2 else 0
                duration = (payload[2] << 24) | (payload[3] << 16) | (payload[4] << 8) | payload[5] if len(payload) >= 6 else 0
                results["passed"] += 1
                results["tests"].append({"num": test_num, "status": "pass", "duration": duration})
                print(f"✅ ({duration}ms)")

            elif cmd == CMD_TEST_FAIL:
                test_num = (payload[0] << 8) | payload[1] if len(payload) >= 2 else 0
                error = payload[2:].decode("utf-8", errors="replace") if len(payload) > 2 else ""
                results["failed"] += 1
                results["tests"].append({"num": test_num, "status": "fail", "error": error})
                print(f"❌ {error}")

            elif cmd == CMD_TEST_SKIP:
                test_num = (payload[0] << 8) | payload[1] if len(payload) >= 2 else 0
                reason = payload[2:].decode("utf-8", errors="replace") if len(payload) > 2 else ""
                results["skipped"] += 1
                results["tests"].append({"num": test_num, "status": "skip", "reason": reason})
                print(f"⏭️  {reason}")

            elif cmd == CMD_TEST_SUITE_END:
                if len(payload) >= 6:
                    results["total"] = (payload[0] << 8) | payload[1]
                    results["passed"] = (payload[2] << 8) | payload[3]
                    results["failed"] = (payload[4] << 8) | payload[5]

            offset += 3 + length

        return results

    def cleanup(self):
        """Clean up processes."""
        print("🧹 Cleaning up...")

        if self.qemu_process:
            self.qemu_process.terminate()
            try:
                self.qemu_process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                self.qemu_process.kill()
            print("   QEMU stopped")

        if self.renode_process:
            self.renode_process.terminate()
            try:
                self.renode_process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                self.renode_process.kill()
            print("   Renode stopped")

    def run(self) -> int:
        """Run the full emulation and return exit code."""
        print("=" * 60)
        print("Cosmos-RPi-Dev-Board PCB Emulation")
        print("=" * 60)
        print(f"ISO: {self.iso_path}")
        print(f"Output: {self.output_path}")
        print(f"Timeout: {self.timeout}s")
        print("=" * 60)

        # Validate inputs
        if not self.validate_inputs():
            return 1

        # For now, skip Renode and just run QEMU directly
        # (until we have actual firmware for ESP32/STM32)
        # In full emulation, we would:
        # 1. Start Renode with both MCUs
        # 2. Inject ISO into ESP32
        # 3. Let ESP32 transfer to STM32 via SPI
        # 4. STM32 writes to SD and starts TFTP
        # 5. QEMU boots from TFTP
        #
        # For now, we just test the QEMU + ISO part:

        print("\n📋 Running simplified emulation (QEMU only)...")
        print("   (Full MCU emulation requires firmware binaries)")

        # Start QEMU with the ISO directly
        if not self.start_qemu():
            return 1

        try:
            # Wait for completion
            if not self.wait_for_completion():
                self.cleanup()
                return 1

            # Parse results
            print("\n📊 Test Results:")
            results = self.parse_results()

            print(f"\n   Suite: {results['suite_name']}")
            print(f"   Total:   {results['total']}")
            print(f"   Passed:  {results['passed']} ✅")
            print(f"   Failed:  {results['failed']} ❌")
            print(f"   Skipped: {results['skipped']} ⏭️")

            # Return exit code based on results
            if results["failed"] > 0:
                return 1
            return 0

        finally:
            self.cleanup()


def main():
    parser = argparse.ArgumentParser(
        description="Run Cosmos-RPi-Dev-Board PCB emulation"
    )
    parser.add_argument(
        "--iso",
        required=True,
        help="Path to kernel ISO file (from GitHub Actions artifact)",
    )
    parser.add_argument(
        "--esp32-firmware",
        help="Path to ESP32-S3 firmware ELF (optional)",
    )
    parser.add_argument(
        "--stm32-firmware",
        help="Path to STM32H563 firmware ELF (optional)",
    )
    parser.add_argument(
        "--output",
        default="uart-output.log",
        help="Path to save UART output log",
    )
    parser.add_argument(
        "--timeout",
        type=int,
        default=120,
        help="Test timeout in seconds",
    )
    parser.add_argument(
        "--ci",
        action="store_true",
        help="CI mode (quieter output)",
    )

    args = parser.parse_args()

    emulator = PCBEmulator(
        iso_path=args.iso,
        esp32_firmware=args.esp32_firmware,
        stm32_firmware=args.stm32_firmware,
        output_path=args.output,
        timeout=args.timeout,
        ci_mode=args.ci,
    )

    # Handle SIGTERM gracefully
    def signal_handler(sig, frame):
        print("\n⚠️  Received termination signal")
        emulator.cleanup()
        sys.exit(1)

    signal.signal(signal.SIGTERM, signal_handler)
    signal.signal(signal.SIGINT, signal_handler)

    sys.exit(emulator.run())


if __name__ == "__main__":
    main()
