// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// On unix make sure to compile using -ldl and -pthread flags.

// Set this value accordingly to your workspace settings
#if defined(_WIN32)
#define PathToLibrary "../Liquip.NativeWrapper/Liquip.NativeWrapper_final.dll"
#else
#define PathToLibrary "../Liquip.NativeWrapper/Liquip.NativeWrapper_final.so"
#endif

#ifdef _WIN32
#include "windows.h"
#define symLoad GetProcAddress
#pragma comment(lib, "ole32.lib")
#else
#include "dlfcn.h"
#include <unistd.h>
#define symLoad dlsym
#define CoTaskMemFree free
#endif

#include <stdio.h>
#include <stdlib.h>

#ifndef F_OK
#define F_OK 0
#endif

// Logging function to standard output
void log_message(const char *message) {
    printf("[LOG]: %s\n", message);
}

int callNativeAdd(char *path, char *funcName, int a, int b);

int main() {
    log_message("Starting the application...");

    // Check if the library path exists
    if (access(PathToLibrary, F_OK) == -1) {
            printf("PathLibrary: %s\n", PathToLibrary);

        log_message("Couldn't find library at the specified path.");
        return 0;
    }

    printf("PathLibrary: %s\n", PathToLibrary);

    log_message("Attempting to call the native 'Native_Add' function...");

    int sum = callNativeAdd(PathToLibrary, "Native_Add", 2, 3);
    if (sum == -1) {
        log_message("Failed to call the native function.");
        return -1;
    }

    printf("The sum is: %d\n", sum);
    log_message("Application completed successfully.");

    return 0;
}

int callNativeAdd(char *path, char *funcName, int a, int b) {
    log_message("Loading the shared library...");

#ifdef _WIN32
    HINSTANCE handle = LoadLibraryA(path);
#else
    void *handle = dlopen(path, RTLD_LAZY | RTLD_GLOBAL);
#endif

    if (!handle) {
        fprintf(stderr, "dlopen failed: %s\n", dlerror());
        log_message("Failed to load the shared library.");
        return -1;
    }

    log_message("Library loaded successfully.");

    typedef int (*myFunc)(int, int);
    myFunc NativeAdd = (myFunc)symLoad(handle, funcName);
    if (!NativeAdd) {
        fprintf(stderr, "dlsym failed: %s\n", dlerror());
        log_message("Failed to load the function from the library.");
        return -1;
    }

    log_message("Function loaded successfully. Calling the function...");

    return NativeAdd(a, b);
}
