/* Test-only image that exports only the ABI discovery symbol. */
#if defined(_WIN32)
#define EXPORT __declspec(dllexport)
#else
#define EXPORT __attribute__((visibility("default")))
#endif

#include <stdint.h>

EXPORT uint32_t dogwoodsharp_abi_version(void)
{
    return 1;
}
