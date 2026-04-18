#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>

static void crash_null_deref() {
    char* p = nullptr;
    std::printf("about to write through nullptr\n");
    p[0] = 'x'; // 0xC0000005
}

static void crash_stack_overflow() {
    volatile char buf[1024];
    buf[0] = 0;
    crash_stack_overflow();
}

static void crash_use_after_free() {
    char* p = static_cast<char*>(std::malloc(16));
    std::strcpy(p, "alive");
    std::printf("before free: %s\n", p);
    std::free(p);
    // May or may not crash depending on allocator; reading freed memory commonly yields 0xDDDDDDDD in debug builds.
    std::printf("after free: %s\n", p);
    p[0] = 'Z';
}

static void crash_heap_corruption() {
    char* p = static_cast<char*>(std::malloc(8));
    // Write past the end to corrupt the CRT heap header.
    for (int i = 0; i < 64; ++i) p[i] = static_cast<char>(0xCC);
    std::free(p);
}

int main(int argc, char** argv) {
    const char* mode = argc > 1 ? argv[1] : "null";
    std::printf("NativeConsole mode=%s pid=%lu\n", mode, static_cast<unsigned long>(_getpid()));

    std::string m = mode;
    if (m == "null")       crash_null_deref();
    else if (m == "stack") crash_stack_overflow();
    else if (m == "uaf")   crash_use_after_free();
    else if (m == "heap")  crash_heap_corruption();
    else {
        std::printf("unknown mode. use: null | stack | uaf | heap\n");
        return 2;
    }
    return 0;
}
