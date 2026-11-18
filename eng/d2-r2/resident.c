/* Linux producer probe. The process owns exactly one native loader reference. */
#include <assert.h>
#include <dlfcn.h>
#include <malloc.h>
#include <pthread.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/resource.h>

typedef struct {
    uint8_t *data;
    uint32_t len;
} buffer;

typedef uint32_t (*version_fn)(void);
typedef uint32_t (*call_fn)(uint32_t, uint32_t, const void *, uint32_t, buffer *);
typedef void (*free_fn)(buffer *);

static call_fn call;
static free_fn release;

static void check(uint32_t operation, const char *request, uint32_t status, const char *fragment)
{
    buffer output = {0};
    assert(call(1, operation, request, (uint32_t)strlen(request), &output) == status);
    if (status == 0) {
        assert(output.data && output.len);
        assert(memmem(output.data, output.len, fragment, strlen(fragment)));
    } else {
        assert(!output.data && output.len == 0);
    }
    release(&output);
    assert(!output.data && output.len == 0);
}

static void *work(void *argument)
{
    size_t count = *(const size_t *)argument;
    const char parse[] = "{\"source\":\"permit(principal,action,resource);\"}";
    const char valid[] = "{\"source\":\"permit(principal,action == App::Action::\\\"read\\\",resource);\","
        "\"policySchema\":\"namespace App { entity User; entity Doc; action \\\"read\\\" appliesTo {"
        "principal: User, resource: Doc, context: {} }; }\"}";
    const char rejected[] = "{\"source\":\"permit(principal,action == Action::\\\"missing\\\",resource);\","
        "\"policySchema\":\"\"}";
    for (size_t i = 0; i < count; ++i) {
        check(1, parse, 0, "\"success\":true");
        check(2, valid, 0, "\"validationPassed\":true");
        check(3, valid, 0, "\"artifactsExported\":true");
        check(2, rejected, 0, "\"validationPassed\":false");
        check(2, "{}", 5, NULL);
    }
    return NULL;
}

static void sample(const char *phase)
{
    struct rusage usage;
    assert(getrusage(RUSAGE_SELF, &usage) == 0);
    const struct mallinfo2 heap = mallinfo2();
    printf("%s heap_in_use_bytes=%zu mmap_bytes=%zu max_rss_kib=%ld\n",
           phase, heap.uordblks, heap.hblkhd, usage.ru_maxrss);
}

int main(int argc, char **argv)
{
    assert(argc == 4);
    size_t total = strtoul(argv[2], NULL, 10);
    assert(total > 0 && total % 4 == 0);
    int concurrent = strcmp(argv[3], "concurrent") == 0;
    assert(concurrent || strcmp(argv[3], "sequential") == 0);

    sample("before-load");
    void *image = dlopen(argv[1], RTLD_NOW | RTLD_LOCAL);
    assert(image);
    version_fn version = (version_fn)dlsym(image, "dogwoodsharp_abi_version");
    call = (call_fn)dlsym(image, "dogwoodsharp_call_v1");
    release = (free_fn)dlsym(image, "dogwoodsharp_free_v1");
    assert(version && version() == 1 && call && release);
    check(0, "{}", 0, "\"abiVersion\":1");
    sample("after-load-and-identity");

    size_t warmup = 1;
    work(&warmup);
    sample("after-first-mixed-iteration");
    warmup = 31;
    work(&warmup);
    sample("after-warmup");

    if (concurrent) {
        pthread_t threads[4];
        size_t per_thread = total / 4;
        for (size_t i = 0; i < 4; ++i) {
            assert(pthread_create(&threads[i], NULL, work, &per_thread) == 0);
        }
        for (size_t i = 0; i < 4; ++i) {
            assert(pthread_join(threads[i], NULL) == 0);
        }
    } else {
        work(&total);
    }

    sample("after-workload");
    printf("RESIDENT PASS mode=%s loads=1 identity=1 warmup=32 iterations=%zu calls=%zu "
           "allocated_responses=%zu release_checks=%zu\n", argv[3], total, 1 + (32 + total) * 5,
           1 + (32 + total) * 4, 1 + (32 + total) * 5);
}
