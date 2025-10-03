#define TRUE 1
#define FALSE 0

#define ASSERT(expr) do { if (!(expr)) { *(volatile int*)0 = 0; } } while (0) // since this will attempt to write to the IVT, it will cause a fault and break into the debugger if one is attached :3


typedef void* HANDLE;

typedef signed char int8_t;
typedef unsigned char uint8_t;
typedef short int16_t;
typedef unsigned short uint16_t;
typedef unsigned int uint32_t;
typedef int int32_t;
typedef long long int64_t;
typedef unsigned long long uint64_t;

typedef unsigned long uintptr_t;
typedef long intptr_t;
typedef unsigned long size_t;
typedef long ssize_t;
typedef long ptrdiff_t;
typedef uint32_t UInt32_BOOL;

uint32_t PalWaitForSingleObjectEx(HANDLE handle, uint32_t milliseconds, UInt32_BOOL alertable)
{
    return 0x00000000; // WAIT_OBJECT_0 (SUCCESS)
}

uint32_t PalCompatibleWaitAny(UInt32_BOOL alertable, uint32_t timeout, uint32_t handleCount, HANDLE* pHandles, UInt32_BOOL allowReentrantWait)
{
    // one handle wait for event is supported
    ASSERT(handleCount == 1);

    return PalWaitForSingleObjectEx(pHandles[0], timeout, alertable);
}

extern uint32_t RhCompatibleReentrantWaitAny(UInt32_BOOL alertable, uint32_t timeout, uint32_t count, HANDLE* pHandles)
{
    return PalCompatibleWaitAny(alertable, timeout, count, pHandles, TRUE);
}