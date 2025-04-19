DOTNET = dotnet
ARGS = publish -c Debug -r linux-x64
CSPROJ = Kernel.csproj

QEMU = qemu-system-x86_64
QEMU_OUTPUT = ./output/Kernel.iso
QEMU_ARGS = -cdrom $(QEMU_OUTPUT)

all: build run

# Build the kernel
build:
	$(DOTNET) $(ARGS) $(CSPROJ)

# Run the kernel (requires build)
run: build
	$(QEMU) $(QEMU_ARGS)

# Clean the build
clean:
	$(DOTNET) clean $(CSPROJ)
	rm -rf ./bin/
	rm -rf ./obj/
	rm -rf ./output/

.PHONY: build run clean