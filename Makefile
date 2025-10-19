build:
	dotnet publish -c Debug -r linux-x64 ./examples/DevKernel/DevKernel.csproj -o ./output-x64

test: build
	qemu-system-x86_64 \
	  -cdrom ./output-x64/DevKernel.iso \
	  -boot d \
	  -m 512M \
	  -serial file:uart.log \
	  -nographic \
	  -no-reboot \
	  -enable-kvm \
	  -cpu host \
	  -no-shutdown &
	QEMU_PID=$!
	sleep 10
	kill $QEMU_PID 2>/dev/null
