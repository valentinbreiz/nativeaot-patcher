ARCH       ?= x64
TIMEOUT    ?= 30
KERNEL     ?= HelloWorld

OUTPUT     := ./output-$(ARCH)

ifeq ($(ARCH),arm64)
  RID          := linux-arm64
  DEFINE       := ARCH_ARM64
  COSMOS_ARCH  := arm64
  QEMU         := qemu-system-aarch64 -M virt,gic-version=3 -cpu cortex-a72 -m 512M \
                    -bios ~/.cosmos/tools/qemu/share/qemu/edk2-aarch64-code.fd
  DEV_QEMU     := qemu-system-aarch64 -M virt,highmem=off,gic-version=3 -cpu cortex-a53 -m 1G \
                    -bios ~/.cosmos/tools/qemu/share/qemu/edk2-aarch64-code.fd
  DEV_ISO_FLAGS := -drive if=none,id=cd,file=$(OUTPUT)/DevKernel.iso \
                   -device virtio-scsi-pci -device scsi-cd,drive=cd,bootindex=0 \
                   -device virtio-keyboard-device -device virtio-mouse-device \
                   -device ramfb -serial stdio
  DEV_READY    := QEMU-DEVKERNEL-ARM64-DEBUG-READY
else
  RID          := linux-x64
  DEFINE       := ARCH_X64
  COSMOS_ARCH  := x64
  QEMU         := qemu-system-x86_64 -enable-kvm -machine accel=kvm -cpu host
  DEV_QEMU     := qemu-system-x86_64 -M q35 -cpu max -m 512M
  DEV_ISO_FLAGS := -drive file=$(OUTPUT)/DevKernel.iso,if=none,id=cosmoscd,format=raw,readonly=on \
                   -device ide-cd,drive=cosmoscd,bootindex=0 \
                   -vga std -device i8042 -serial stdio
  DEV_READY    := QEMU-DEVKERNEL-DEBUG-READY
endif

DEVKERNEL  := ./examples/DevKernel/DevKernel.csproj
TEST_ENGINE := ./tests/Cosmos.TestRunner.Engine/Cosmos.TestRunner.Engine.csproj

AHCI_IMG   := disk-ahci.img
NVME_IMG   := disk-nvme.img
DEV_DISK_FLAGS := -drive file=$(AHCI_IMG),if=none,id=ahcidisk,format=raw \
                  -device ich9-ahci,id=ahci0 \
                  -device ide-hd,drive=ahcidisk,bus=ahci0.0 \
                  -drive file=$(NVME_IMG),if=none,id=nvmedisk,format=raw \
                  -device nvme,drive=nvmedisk,serial=cosmos-nvme

.PHONY: setup build clean distclean run run-dev debug-dev disks test test-cache

setup:
	./.devcontainer/postCreateCommand.sh

build:
	dotnet publish -c Debug -r $(RID) \
		-p:DefineConstants="$(DEFINE)" -p:CosmosArch=$(COSMOS_ARCH) \
		$(DEVKERNEL) -o $(OUTPUT)

clean:
	rm -rf ./output-x64 ./output-arm64 uart.log

distclean: clean
	rm -rf ./artifacts
	dotnet nuget remove source local-packages 2>/dev/null || true
	rm -rf ~/.nuget/packages/cosmos.* 2>/dev/null || true

run: build
	@echo "Starting QEMU ($(ARCH))... Press Ctrl+A X to exit."
	@QEMU_PID=""; \
	trap 'test -n "$$QEMU_PID" && kill $$QEMU_PID 2>/dev/null || true' EXIT; \
	$(QEMU) \
	  -cdrom $(OUTPUT)/DevKernel.iso \
	  -boot d \
	  -m 512M \
	  -serial file:uart.log \
	  -nographic \
	  -no-reboot \
	  -no-shutdown & \
	QEMU_PID=$$!; \
	sleep $(TIMEOUT); \
	echo "Stopping QEMU..."; \
	kill $$QEMU_PID 2>/dev/null || true

$(AHCI_IMG) $(NVME_IMG):
	truncate -s 256M $@

disks: $(AHCI_IMG) $(NVME_IMG)

run-dev: build disks
	$(DEV_QEMU) $(DEV_ISO_FLAGS) $(DEV_DISK_FLAGS) -no-reboot -no-shutdown

debug-dev: build disks
	$(DEV_QEMU) $(DEV_ISO_FLAGS) $(DEV_DISK_FLAGS) -s -S -no-reboot -no-shutdown & \
	sleep 1; \
	echo "$(DEV_READY)"; \
	wait

test:
	dotnet build $(TEST_ENGINE) -c Debug
	dotnet run --project $(TEST_ENGINE) --no-build \
		-- tests/Kernels/Cosmos.Kernel.Tests.$(KERNEL) $(ARCH) $(TIMEOUT) \
		test-results-$(KERNEL)-$(ARCH).xml ci

test-cache:
	dotnet test tests/Cosmos.Tests.BuildCache/ -c Debug
