TARGET = output/Kernel.efi
CC = gcc
CFLAGS = -O2 -nostdlib -I/usr/include/efi -I/usr/include/efi/x86_64
LDFLAGS = -T /usr/lib/elf_x86_64_efi.lds -shared -Bsymbolic -nostdlib -L/usr/lib -lefi -lgnuefi

all: $(TARGET)
	@echo "Compilation complete: $(TARGET)"

$(TARGET): src/Kernel.c
	@mkdir -p output
	$(CC) $(CFLAGS) $(LDFLAGS) $< -o $(TARGET)

clean:
	rm -rf output