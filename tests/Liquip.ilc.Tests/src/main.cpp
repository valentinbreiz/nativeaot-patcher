// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// On unix make sure to compile using -ldl and -pthread flags.

#include <cstddef>
#include <cstdlib>
#include <cstdio>
#include <cstring>
#include <filesystem>
#include <unistd.h>
#include "dlfcn.h"
#include "glob.h"

#define symLoad dlsym
#define PathLibraryDir "../Liquip.NativeWrapper/bin/Debug/net8.0/native/"
#define CoTaskMemFree free

#ifndef F_OK
#define F_OK 0
#endif

void log_message(const char *message) { 
    printf("[LOG]: %s\n", message); 
}

int callNativeAdd(char *path, char *funcName, int a, int b);

int main() {
    log_message("Starting the application...");
    
    char pattern[256];
    snprintf(pattern, sizeof(pattern), "%s/*.so", PathLibraryDir);
    
    glob_t glob_result;
    int ret = glob(pattern, 0, NULL, &glob_result);
    if (ret != 0 || glob_result.gl_pathc == 0) {
        log_message("Couldn't find library at the specified path.");
        globfree(&glob_result);
        return EXIT_FAILURE;
    }
    
    char* pathToLibrary = glob_result.gl_pathv[0];
    printf("PathLibrary: %s\n", pathToLibrary);
    log_message("Attempting to call the native 'Native_Add' function...");
    
    int sum = callNativeAdd(pathToLibrary, "Native_Add", 2, 3);
    if (sum == -1) {
        log_message("Failed to call the native function.");
        globfree(&glob_result);
        return EXIT_FAILURE;
    }
    
    printf("The sum is: %d\n", sum);
    log_message("Application completed successfully.");
    
    globfree(&glob_result);
    return EXIT_SUCCESS;
}

int callNativeAdd(char *path, char *funcName, int a, int b) {
    log_message("Loading the shared library...");

#ifdef _WIN32
    HINSTANCE handle = LoadLibraryA(path);
#else
    void *handle = dlopen(path, RTLD_LAZY | RTLD_GLOBAL);
#endif

    if (!handle) {
#ifdef _WIN32
        fprintf(stderr, "LoadLibraryA failed.\n");
#else
        fprintf(stderr, "dlopen failed: %s\n", dlerror());
#endif
        log_message("Failed to load the shared library.");
        return -1;
    }
    
    log_message("Library loaded successfully.");
    
    typedef int (*myFunc)(int, int);
    myFunc NativeAdd = (myFunc)symLoad(handle, funcName);
    if (!NativeAdd) {
#ifdef _WIN32
        fprintf(stderr, "GetProcAddress failed.\n");
#else
        fprintf(stderr, "dlsym failed: %s\n", dlerror());
#endif
        log_message("Failed to load the function from the library.");
        return -1;
    }
    
    log_message("Function loaded successfully. Calling the function...");
    return NativeAdd(a, b);
}
