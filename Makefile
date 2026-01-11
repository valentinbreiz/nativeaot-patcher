build:
	dotnet publish -c Debug -r linux-x64 ./examples/DevKernel/DevKernel.csproj -p:DefineConstants=ARCH_X64 -p:CosmosArch=x64 -o ./output-x64

test: build
	@echo "Starting QEMU..."
	@QEMU_PID=""; \
	trap 'test -n "$$QEMU_PID" && kill $$QEMU_PID 2>/dev/null || true' EXIT; \
	qemu-system-x86_64 \
	  -smp cores=2,sockets=1,threads=1 \
	  -cdrom ./output-x64/DevKernel.iso \
	  -boot d \
	  -m 512M \
	  -serial file:uart.log \
	  -nographic \
	  -no-reboot \
	  -enable-kvm \
	  -machine accel=kvm \
	  -cpu host & \
	QEMU_PID=$$!; \
	sleep 20; \
	echo "Stopping QEMU..."; \
	kill $$QEMU_PID 2>/dev/null || true

run: build
	@echo "Starting QEMU..."
	qemu-system-x86_64 \
	  -smp cores=2,sockets=1,threads=1 \
	  -cdrom ./output-x64/DevKernel.iso \
	  -boot d \
	  -m 512M \
	  -serial file:uart.log \
	  -no-reboot \
	  -enable-kvm \
	  -machine accel=kvm \
	  -cpu host
