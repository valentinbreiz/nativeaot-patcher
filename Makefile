ARCH       ?= x64
TIMEOUT    ?= 30
KERNEL     ?= HelloWorld

ifeq ($(ARCH),arm64)
  RID          := linux-arm64
  DEFINE       := ARCH_ARM64
  COSMOS_ARCH  := arm64
  QEMU         := qemu-system-aarch64 -M virt -cpu cortex-a72 -m 512M \
                    -bios ~/.cosmos/tools/qemu/share/qemu/edk2-aarch64-code.fd
else
  RID          := linux-x64
  DEFINE       := ARCH_X64
  COSMOS_ARCH  := x64
  QEMU         := qemu-system-x86_64 -enable-kvm -machine accel=kvm -cpu host
endif

OUTPUT     := ./output-$(ARCH)
DEVKERNEL  := ./examples/DevKernel/DevKernel.csproj
TEST_ENGINE := ./tests/Cosmos.TestRunner.Engine/Cosmos.TestRunner.Engine.csproj

.PHONY: setup build clean distclean run test test-cache

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

test:
	dotnet test --project tests/Kernels/Cosmos.Kernel.Tests.$(KERNEL) \
		--report-xml --report-xml-filename test-results-$(KERNEL)-$(ARCH).xml \
		--timeout $(TIMEOUT)s --kernel-arch $(ARCH) \
		--results-directory "./tests/Kernels/TestResults"

test-cache:
	dotnet test tests/Cosmos.Tests.BuildCache/ -c Debug
