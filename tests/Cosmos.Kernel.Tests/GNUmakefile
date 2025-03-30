# Nuke built-in rules and variables.
MAKEFLAGS += -rR
.SUFFIXES:

# Target architecture to build for. Default to x86_64.
ARCH := x86_64

# Default user QEMU flags. These are appended to the QEMU command calls.
QEMUFLAGS := -m 2G
override IMAGE_NAME := Kernel-$(ARCH)

# Toolchain for building the 'limine' executable for the host.
HOST_CC := cc
HOST_CFLAGS := -g -O2 -pipe
HOST_CPPFLAGS :=
HOST_LDFLAGS :=
HOST_LIBS :=

# Integration of Cosmos.Entry
DOTNET_PROJECT := Cosmos.Entry/Cosmos.Entry.csproj
DOTNET_OBJ := Cosmos.Entry/bin/Debug/net8.0/cosmos/native/Cosmos.Entry_final.o
DOTNET_OUTPUT_DIR := Cosmos.Entry/bin/Debug/net8.0/native
DOTNET_FINAL := $(DOTNET_OUTPUT_DIR)/Cosmos.Entry_final.o

LDFLAGS += $(DOTNET_FINAL) -Wl,--undefined=dotnet_main

.PHONY: all dotnet kernel kernel-deps

all: output/$(IMAGE_NAME).iso

dotnet:
	@echo "=== Build Cosmos.Entry (NativeAOT) ==="
	dotnet build $(DOTNET_PROJECT) --configuration Debug --verbosity detailed

$(DOTNET_FINAL): dotnet
	@echo "Ensuring Cosmos.Entry final shared library exists: $@"

kernel-deps:
	@echo "=== Getting kernel dependencies ==="
	./Cosmos.Native/get-deps
	touch kernel-deps

kernel: kernel-deps dotnet
	@echo "=== Building Kernel ==="
	$(MAKE) -C Cosmos.Native

output/$(IMAGE_NAME).iso: limine/limine kernel dotnet $(DOTNET_FINAL)
	@echo "=== Generating ISO Image ==="
	rm -rf output/iso_root
	mkdir -p output/iso_root/boot output/iso_root/boot/limine output/iso_root/EFI/BOOT output
	cp -v Cosmos.Native/bin-$(ARCH)/kernel output/iso_root/boot/
	cp -v limine.conf output/iso_root/boot/limine/
ifeq ($(ARCH),x86_64)
	cp -v limine/limine-bios.sys limine/limine-bios-cd.bin limine/limine-uefi-cd.bin output/iso_root/boot/limine/
	cp -v limine/BOOTX64.EFI output/iso_root/EFI/BOOT/
	cp -v limine/BOOTIA32.EFI output/iso_root/EFI/BOOT/
	xorriso -as mkisofs -R -r -J -b boot/limine/limine-bios-cd.bin \
		-no-emul-boot -boot-load-size 4 -boot-info-table -hfsplus \
		-apm-block-size 2048 --efi-boot boot/limine/limine-uefi-cd.bin \
		-efi-boot-part --efi-boot-image --protective-msdos-label \
		output/iso_root -o output/$(IMAGE_NAME).iso
	./limine/limine bios-install output/$(IMAGE_NAME).iso
endif
ifeq ($(ARCH),aarch64)
	cp -v limine/limine-uefi-cd.bin output/iso_root/boot/limine/
	cp -v limine/BOOTAA64.EFI output/iso_root/EFI/BOOT/
	mkdir -p output
	xorriso -as mkisofs -R -r -J \
		-hfsplus -apm-block-size 2048 \
		--efi-boot boot/limine/limine-uefi-cd.bin \
		-efi-boot-part --efi-boot-image --protective-msdos-label \
		output/iso_root -o output/$(IMAGE_NAME).iso
endif
	rm -rf output/iso_root

limine/limine:
	@echo "=== Building Limine ==="
	rm -rf limine
	git clone https://github.com/limine-bootloader/limine.git --branch=v9.x-binary --depth=1
	$(MAKE) -C limine \
		CC="$(HOST_CC)" \
		CFLAGS="$(HOST_CFLAGS)" \
		CPPFLAGS="$(HOST_CPPFLAGS)" \
		LDFLAGS="$(HOST_LDFLAGS)" \
		LIBS="$(HOST_LIBS)"

ovmf/ovmf-code-$(ARCH).fd:
	@echo "=== Downloading OVMF Firmware ==="
	mkdir -p ovmf
	curl -Lo $@ https://github.com/osdev0/edk2-ovmf-nightly/releases/latest/download/ovmf-code-$(ARCH).fd
	case "$(ARCH)" in \
		aarch64) dd if=/dev/zero of=$@ bs=1 count=0 seek=67108864 2>/dev/null;; \
		riscv64) dd if=/dev/zero of=$@ bs=1 count=0 seek=33554432 2>/dev/null;; \
	esac

clean:
	$(MAKE) -C Cosmos.Native clean
	dotnet clean $(DOTNET_PROJECT)
	rm -rf output/iso_root output/$(IMAGE_NAME).iso

distclean:
	$(MAKE) -C Cosmos.Native distclean
	rm -rf output/iso_root output/*.iso kernel-deps limine ovmf