TARGET = output/Kernel.efi
ISO = output/Kernel.iso
CC = gcc
CFLAGS = -O2 -nostdlib -I/usr/include/efi -I/usr/include/efi/x86_64
LDFLAGS = -T /usr/lib/elf_x86_64_efi.lds -shared -Bsymbolic -nostdlib -L/usr/lib -lefi -lgnuefi

all: iso
	@echo "Build complete: $(ISO)"

$(TARGET): src/Kernel.c
	@mkdir -p output
	$(CC) $(CFLAGS) $(LDFLAGS) $< -o $(TARGET)

iso: $(TARGET)
	@mkdir -p output/EFI/BOOT
	@cp $(TARGET) output/EFI/BOOT/BOOTX64.EFI
	@mkdir -p output
	xorriso -as mkisofs -R -f -e EFI/BOOT/BOOTX64.EFI -no-emul-boot -o $(ISO) output
	@echo "ISO created: $(ISO)"

run: iso
	qemu-system-x86_64 -bios /usr/share/OVMF/OVMF_CODE.fd -cdrom $(ISO)

clean:
	rm -rf output