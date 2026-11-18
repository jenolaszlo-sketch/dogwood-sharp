/* Linux consumer. Default runs retain the verified handle through process exit. */
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
    uint32_t length;
} response;

typedef uint32_t (*abi_fn)(void);
typedef uint32_t (*call_fn)(uint32_t, uint32_t, const void *, uint32_t, response *);
typedef void (*release_fn)(response *);

typedef struct {
    call_fn invoke;
    release_fn release;
    size_t count;
    size_t calls;
    size_t released;
} worker;

static void check(worker *w, uint32_t op, const char *request, uint32_t expected, const char *marker)
{
    response value = {0};
    assert(w->invoke(1, op, request, (uint32_t)strlen(request), &value) == expected);
    ++w->calls;
    if (expected == 0) {
        assert(value.data && value.length);
        assert(memmem(value.data, value.length, marker, strlen(marker)));
        ++w->released;
    } else {
        assert(!value.data && value.length == 0);
    }
    w->release(&value);
    assert(!value.data && value.length == 0);
}

static void *exercise(void *state)
{
    worker *w = state;
    const char parse[] = "{\"source\":\"forbid (principal, action, resource);\"}";
    const char schema[] = "entity Viewer; entity Note; action \\\"inspect\\\" appliesTo {principal: Viewer, resource: Note};";
    char permitted[512], rejected[512];
    int length = snprintf(permitted, sizeof permitted, "{\"source\":\"permit(principal, action == Action::\\\"inspect\\\", resource);\",\"policySchema\":\"%s\"}", schema);
    assert(length > 0 && (size_t)length < sizeof permitted);
    length = snprintf(rejected, sizeof rejected, "{\"source\":\"permit(principal, action == Action::\\\"absent\\\", resource);\",\"policySchema\":\"%s\"}", schema);
    assert(length > 0 && (size_t)length < sizeof rejected);

    for (size_t i = 0; i < w->count; ++i) {
        check(w, 1, parse, 0, "\"success\":true");
        check(w, 2, permitted, 0, "\"validationPassed\":true");
        check(w, 3, permitted, 0, "\"artifactsExported\":true");
        check(w, 2, rejected, 0, "\"validationPassed\":false");
        check(w, 3, "{", 5, NULL);
    }
    return NULL;
}

static void snapshot(const char *phase)
{
    struct rusage usage;
    assert(getrusage(RUSAGE_SELF, &usage) == 0);
    struct mallinfo2 memory = mallinfo2();
    printf("%s heap_in_use=%zu mapped_heap=%zu peak_rss_kib=%ld\n",
           phase, memory.uordblks, memory.hblkhd, usage.ru_maxrss);
}

int main(int argc, char *argv[])
{
    assert(argc == 4);
    const char *mode = argv[2];
    size_t rounds = strtoul(argv[3], NULL, 10);
    assert(rounds >= 8 && rounds <= 2048 && rounds % 4 == 0);
    snapshot("before-open");
    void *handle = dlopen(argv[1], RTLD_NOW | RTLD_LOCAL);
    assert(handle);
    abi_fn abi = (abi_fn)dlsym(handle, "dogwoodsharp_abi_version");
    worker setup = { .invoke = (call_fn)dlsym(handle, "dogwoodsharp_call_v1"),
                     .release = (release_fn)dlsym(handle, "dogwoodsharp_free_v1"),
                     .count = 32 };
    assert(abi && abi() == 1 && setup.invoke && setup.release);
    check(&setup, 0, "{}", 0, "\"abiVersion\":1");
    snapshot("after-identity");
    exercise(&setup);
    snapshot("after-warmup");

    size_t calls = 0, released = 0, started = 0, max_workers = 0;
    if (!strcmp(mode, "fixed") || !strcmp(mode, "churn")) {
        int churn = !strcmp(mode, "churn");
        max_workers = churn ? 2 : 4;
        size_t waves = churn ? rounds / 2 : 1;
        for (size_t wave = 0; wave < waves; ++wave) {
            pthread_t threads[4];
            worker workers[4];
            for (size_t n = 0; n < max_workers; ++n) {
                workers[n] = (worker){ .invoke = setup.invoke, .release = setup.release,
                                       .count = rounds / (waves * max_workers) };
                assert(pthread_create(&threads[n], NULL, exercise, &workers[n]) == 0);
                ++started;
            }
            for (size_t n = 0; n < max_workers; ++n) {
                assert(pthread_join(threads[n], NULL) == 0);
                calls += workers[n].calls;
                released += workers[n].released;
            }
        }
    } else {
        assert(!strcmp(mode, "sequential") || !strcmp(mode, "unload"));
        worker measured = { .invoke = setup.invoke, .release = setup.release, .count = rounds };
        exercise(&measured);
        calls = measured.calls;
        released = measured.released;
    }

    assert(setup.calls == 161 && setup.released == 129);
    assert(calls == rounds * 5 && released == rounds * 4);
    snapshot("after-measurement");
    if (!strcmp(mode, "unload")) {
        assert(dlclose(handle) == 0);
    }
    printf("PASS mode=%s rounds=%zu workers_started=%zu max_workers=%zu loads=1 abi=1 calls=%zu populated_releases=%zu empty_releases=%zu\n",
           mode, rounds, started, max_workers, calls + setup.calls, released + setup.released,
           calls + setup.calls - released - setup.released);
}
