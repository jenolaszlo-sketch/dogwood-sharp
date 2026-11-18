#include <assert.h>
#include <dlfcn.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>

typedef struct {
    uint8_t *data;
    uint32_t len;
} owned;

typedef uint32_t (*call_fn)(uint32_t, uint32_t, const void *, uint32_t, owned *);
typedef void (*free_fn)(owned *);

static void check(owned result, const char *fragment)
{
    assert(result.data && result.len);
    assert(memmem(result.data, result.len, fragment, strlen(fragment)));
}

static void release(free_fn free_buffer, owned *result)
{
    free_buffer(result);
    assert(!result->data && result->len == 0);
    free_buffer(result);
}

static void exercise(call_fn call, free_fn free_buffer, int parse_only)
{
    const char parse[] = "{\"source\":\"permit(principal,action,resource);\"}";
    const char validate[] = "{\"source\":\"\",\"policySchema\":\"\"}";
    const char reject[] = "{\"source\":\"permit(principal, action == Action::\\\"missing\\\", resource);\",\"policySchema\":\"\"}";
    for (size_t i = 0; i < 32; i++) {
        owned d1 = {0}, d2 = {0};
        assert(call(1, 1, parse, sizeof parse - 1, &d1) == 0);
        if (parse_only) {
            check(d1, "\"success\":true");
            release(free_buffer, &d1);
            continue;
        }
        assert(call(1, 3, validate, sizeof validate - 1, &d2) == 0);
        assert(d1.data != d2.data);
        check(d1, "\"success\":true");
        release(free_buffer, &d1);
        check(d2, "\"artifactsExported\":true");
        release(free_buffer, &d2);
        assert(call(1, 3, reject, sizeof reject - 1, &d2) == 0);
        check(d2, "\"artifactsExported\":false");
        release(free_buffer, &d2);
        assert(call(1, 2, "{}", 2, &d2) == 5);
        assert(!d2.data && d2.len == 0);
        release(free_buffer, &d2);
    }
    free_buffer(NULL);
}

int main(int argc, char **argv)
{
    assert(argc == 3);
    int loader_only = strcmp(argv[2], "loader-only") == 0;
    int parse_only = strcmp(argv[2], "parse-unload") == 0;
    int resident = strcmp(argv[2], "resident") == 0;
    assert(loader_only || parse_only || resident || strcmp(argv[2], "mixed-unload") == 0);
    void *image = dlopen(argv[1], RTLD_NOW);
    assert(image);
    call_fn call = (call_fn)dlsym(image, "dogwoodsharp_call_v1");
    free_fn free_buffer = (free_fn)dlsym(image, "dogwoodsharp_free_v1");
    assert(call && free_buffer);
    if (!loader_only)
        exercise(call, free_buffer, parse_only);
    if (!resident)
        assert(dlclose(image) == 0);
    printf("INDEPENDENT OWNERSHIP ASSERTIONS PASS control=%s calls=%d\n", argv[2],
           loader_only ? 0 : parse_only ? 32 : 128);
}
