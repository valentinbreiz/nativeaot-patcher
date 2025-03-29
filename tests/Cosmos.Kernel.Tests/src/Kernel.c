#include <efi.h>
#include <efilib.h>

EFI_STATUS EFIAPI efi_main(EFI_HANDLE imageHandle, EFI_SYSTEM_TABLE *systemTable) {
    InitializeLib(imageHandle, systemTable);

    Print((CHAR16 *)L"Hello, UEFI Kernel Boot ZBI!\n");

    while (1) {
        __asm__("hlt");
    }

    return EFI_SUCCESS;
}
