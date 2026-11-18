/* macOS failed-initialization substitute. This image declares no TLS. */
#include <assert.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>

typedef struct {
    uint8_t *data;
    uint32_t length;
} buffer;

static void event(const char *line)
{
    FILE *log = fopen(getenv("Q2_EVENTS"), "a");
    assert(log);
    assert(fputs(line, log) >= 0);
    assert(fclose(log) == 0);
}

uint32_t dogwoodsharp_abi_version(void)
{
    event("abi\n");
    return 1;
}

uint32_t dogwoodsharp_call_v1(uint32_t abi, uint32_t op, const uint8_t *input,
                          uint32_t length, buffer *output)
{
    assert(abi == 1 && op == 0 && input && length == 2 && output);
    FILE *identity = fopen(getenv("Q2_IDENTITY"), "rb");
    assert(identity);
    uint8_t *reply = malloc(4096);
    assert(reply);
    size_t count = fread(reply, 1, 4096, identity);
    assert(count > 0 && count < 4096 && !ferror(identity));
    assert(fclose(identity) == 0);
    *output = (buffer){reply, (uint32_t)count};
    event("alloc 0 1\n");
    return 0;
}

void dogwoodsharp_free_v1(buffer *output)
{
    assert(output && output->data && output->length);
    free(output->data);
    *output = (buffer){0};
    event("free 0 1\n");
}
