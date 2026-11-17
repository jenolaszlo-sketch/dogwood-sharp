/*
 * Independent C ABI v1 consumer for the DogwoodSharp native bridge.
 *
 * This program is written only against docs/architecture/native-interop.md
 * and docs/architecture/native-artifacts.md. It does not include or link any
 * DogwoodSharp source.
 *
 * Modes:
 *   abi_probe selftest <library-path> [expected-target]
 *   abi_probe probe <manifest.tsv> <results.bin>
 *
 * The manifest has one "id<TAB>operation<TAB>request-file" record per line.
 * The probe writes a small little-endian binary result file that the Python
 * comparator decodes:
 *
 *   "D1QP" u32(1) u32(caseCount)
 *   per case: u32(idLen) id u32(status) u32(bodyLen) body u32(slotEmpty)
 */

#if defined(_WIN32)
#define _CRT_SECURE_NO_WARNINGS
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#else
#include <dlfcn.h>
#include <pthread.h>
#endif

#include <stddef.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#define ABI_VERSION 1u
#define SOURCE_LIMIT (1024u * 1024u)
#define REQUEST_LIMIT (6u * SOURCE_LIMIT + 64u)
#define RESPONSE_LIMIT (2u * SOURCE_LIMIT)

typedef struct {
    uint8_t *data;
    uint32_t len;
} dw_buffer;

_Static_assert(sizeof(uint32_t) == 4, "uint32_t must be 4 bytes");
_Static_assert(sizeof(dw_buffer) == 16, "ABI v1 buffer must be 16 bytes");
_Static_assert(offsetof(dw_buffer, data) == 0, "ABI v1 data must be at offset 0");
_Static_assert(offsetof(dw_buffer, len) == 8, "ABI v1 len must be at offset 8");

typedef uint32_t (*abi_version_fn)(void);
typedef uint32_t (*call_fn)(uint32_t, uint32_t, const uint8_t *, uint32_t, dw_buffer *);
typedef void (*free_fn)(dw_buffer *);

static abi_version_fn fn_abi_version;
static call_fn fn_call;
static free_fn fn_free;

static int failures;

static void pass(const char *name)
{
    printf("PASS %s\n", name);
}

static void fail(const char *name, const char *detail)
{
    printf("FAIL %s%s%s\n", name, detail && *detail ? ": " : "", detail ? detail : "");
    failures++;
}

static void check(int condition, const char *name)
{
    if (condition) {
        pass(name);
    } else {
        fail(name, "assertion failed");
    }
}

static int body_contains(const dw_buffer *buffer, const char *text)
{
    size_t needle = strlen(text);
    size_t i;
    if (needle == 0 || needle > buffer->len) {
        return 0;
    }
    for (i = 0; i + needle <= buffer->len; i++) {
        if (memcmp(buffer->data + i, text, needle) == 0) {
            return 1;
        }
    }
    return 0;
}

static uint32_t call(uint32_t abi, uint32_t operation, const void *input, uint32_t length, dw_buffer *output)
{
    return fn_call(abi, operation, (const uint8_t *)input, length, output);
}

static void release(dw_buffer *buffer)
{
    fn_free(buffer);
}

static int slot_empty(const dw_buffer *buffer)
{
    return buffer->data == NULL && buffer->len == 0;
}

/* Exercise one expected-nonzero call and require the output slot to be empty. */
static void reject(const char *name, uint32_t abi, uint32_t operation, const void *input, uint32_t length,
                   uint32_t expected)
{
    dw_buffer buffer;
    uint32_t status;
    char detail[96];

    buffer.data = (uint8_t *)(uintptr_t)1; /* no live allocation; bridge must overwrite */
    buffer.len = 0xdeadbeefu;
    status = call(abi, operation, input, length, &buffer);
    if (status != expected) {
        snprintf(detail, sizeof detail, "expected status %u, got %u", expected, status);
        fail(name, detail);
        /* Never free a descriptor the bridge did not produce. */
        return;
    }
    if (!slot_empty(&buffer)) {
        fail(name, "nonzero status left a nonempty output");
        return;
    }
    pass(name);
}

static void load_library(const char *path)
{
#if defined(_WIN32)
    HMODULE handle = LoadLibraryA(path);
    union {
        FARPROC address;
        abi_version_fn abi;
    } a;
    union {
        FARPROC address;
        call_fn call;
    } c;
    union {
        FARPROC address;
        free_fn free_slot;
    } f;
    if (handle == NULL) {
        fprintf(stderr, "FAIL load_library: LoadLibraryA error %lu\n", (unsigned long)GetLastError());
        exit(2);
    }
    a.address = GetProcAddress(handle, "dogwoodsharp_abi_version");
    c.address = GetProcAddress(handle, "dogwoodsharp_call_v1");
    f.address = GetProcAddress(handle, "dogwoodsharp_free_v1");
    fn_abi_version = a.abi;
    fn_call = c.call;
    fn_free = f.free_slot;
#else
    void *handle = dlopen(path, RTLD_NOW | RTLD_LOCAL);
    if (handle == NULL) {
        fprintf(stderr, "FAIL load_library: %s\n", dlerror());
        exit(2);
    }
    fn_abi_version = (abi_version_fn)dlsym(handle, "dogwoodsharp_abi_version");
    fn_call = (call_fn)dlsym(handle, "dogwoodsharp_call_v1");
    fn_free = (free_fn)dlsym(handle, "dogwoodsharp_free_v1");
#endif
    if (fn_abi_version == NULL) {
        fail("export_abi_version", "symbol missing");
    } else {
        pass("export_abi_version");
    }
    if (fn_call == NULL) {
        fail("export_call_v1", "symbol missing");
    } else {
        pass("export_call_v1");
    }
    if (fn_free == NULL) {
        fail("export_free_v1", "symbol missing");
    } else {
        pass("export_free_v1");
    }
    if (fn_abi_version == NULL || fn_call == NULL || fn_free == NULL) {
        exit(2);
    }
}

static void test_identity(const char *expected_target)
{
    dw_buffer buffer = {NULL, 0};
    uint32_t status = call(ABI_VERSION, 0, "{}", 2, &buffer);
    check(status == 0, "identity_status_ok");
    if (status != 0) {
        return;
    }
    check(slot_empty(&buffer) == 0, "identity_buffer_nonempty");
    check(buffer.len > 0 && buffer.len <= RESPONSE_LIMIT, "identity_response_bounded");
    check(body_contains(&buffer, "\"abiVersion\":1"), "identity_abi");
    check(body_contains(&buffer, "\"bridgeVersion\":\"0.1.0\""), "identity_bridge");
    check(body_contains(&buffer, "\"dogwoodRevision\":\"929f49e7305534ac61ef06cf8e47269c596dc851\""), "identity_revision");
    check(body_contains(&buffer, "\"dogwoodVersion\":\"1.1.2\""), "identity_dogwood");
    check(body_contains(&buffer, "\"cedarVersion\":\"4.11.0\""), "identity_cedar");
    check(body_contains(&buffer, "\"rustVersion\":\"1.99.1\""), "identity_rust");
    check(body_contains(&buffer, "\"dogwoodFeatures\":[]"), "identity_dogwood_features");
    check(body_contains(&buffer, "\"cedarFeatures\":[\"datetime\",\"decimal\",\"ipaddr\"]"), "identity_cedar_features");
    check(body_contains(&buffer, "\"target\":\""), "identity_target_present");
    if (expected_target != NULL) {
        char expected[160];
        snprintf(expected, sizeof expected, "\"target\":\"%s\"", expected_target);
        check(body_contains(&buffer, expected), "identity_target_value");
    }
    release(&buffer);
    check(slot_empty(&buffer), "identity_free_resets");
}

static void test_rejections(void)
{
    static const uint8_t invalid_utf8[] = {'{', 0xff, '}'};
    static const uint8_t bom_json[] = {0xef, 0xbb, 0xbf, '{', '}'};
    static const uint8_t bare_invalid_utf8[] = {0xff};
    static const char *valid_parse_trailing = "{\"source\":\"permit(principal, action, resource);\"} \t\r\n";

    {
        uint32_t status = call(ABI_VERSION, 0, "{}", 2, NULL);
        check(status == 1, "reject_null_output");
    }
    reject("reject_null_input_nonzero_length", ABI_VERSION, 0, NULL, 1, 1);
    reject("reject_null_input_zero_length", ABI_VERSION, 0, NULL, 0, 1);
    reject("reject_empty_input", ABI_VERSION, 1, "", 0, 1);
    reject("reject_abi_zero", 0, 0, "{}", 2, 2);
    reject("reject_abi_two", 2, 0, "{}", 2, 2);
    reject("reject_abi_precedes_input", 2, 1, NULL, 0, 2);
    reject("reject_operation_two", ABI_VERSION, 2, "{}", 2, 3);
    reject("reject_operation_precedes_input", ABI_VERSION, 2, "", 0, 3);
    reject("reject_operation_precedes_limit", ABI_VERSION, 0xffffffffu, "{}", 2, 3);
    reject("reject_invalid_utf8", ABI_VERSION, 1, invalid_utf8, sizeof invalid_utf8, 4);
    reject("reject_invalid_utf8_shape", ABI_VERSION, 1, bare_invalid_utf8, sizeof bare_invalid_utf8, 4);
    reject("reject_json_bom", ABI_VERSION, 0, bom_json, sizeof bom_json, 5);
    reject("reject_empty_object_for_parse", ABI_VERSION, 1, "{}", 2, 5);
    reject("reject_json_array", ABI_VERSION, 1, "[]", 2, 5);
    reject("reject_json_null", ABI_VERSION, 1, "null", 4, 5);
    reject("reject_trailing_value", ABI_VERSION, 0, "{} {}", 5, 5);
    reject("reject_unknown_version_field", ABI_VERSION, 0, "{\"extra\":1}", 11, 5);
    reject("reject_duplicate_source", ABI_VERSION, 1, "{\"source\":\"a\",\"source\":\"b\"}", 27, 5);
    reject("reject_lone_high_surrogate", ABI_VERSION, 1, "{\"source\":\"\\ud800\"}", 19, 5);
    reject("reject_lone_low_surrogate", ABI_VERSION, 1, "{\"source\":\"\\udc00\"}", 19, 5);
    reject("reject_null_source", ABI_VERSION, 1, "{\"source\":null}", 16, 5);
    reject("reject_numeric_source", ABI_VERSION, 1, "{\"source\":1}", 13, 5);
    reject("reject_whitespace_only", ABI_VERSION, 1, "  \r\n\t", 5, 5);
    reject("reject_overlong_request", ABI_VERSION, 0, NULL, REQUEST_LIMIT + 1u, 6);
    {
        size_t n = (size_t)SOURCE_LIMIT + 1u;
        uint8_t *source = (uint8_t *)malloc(n);
        uint8_t *request;
        size_t request_len;
        if (source == NULL) {
            fail("reject_source_limit_plus_one", "allocation failure");
            return;
        }
        source[0] = '/';
        source[1] = '/';
        memset(source + 2, 'a', n - 2);
        request_len = n + strlen("{\"source\":\"\"}");
        request = (uint8_t *)malloc(request_len);
        if (request == NULL) {
            free(source);
            fail("reject_source_limit_plus_one", "allocation failure");
            return;
        }
        memcpy(request, "{\"source\":\"", 11);
        memcpy(request + 11, source, n);
        memcpy(request + 11 + n, "\"}", 2);
        reject("reject_source_limit_plus_one", ABI_VERSION, 1, request, (uint32_t)request_len, 6);
        free(request);
        free(source);
    }

    /* Leading and trailing JSON whitespace is part of the envelope, not a value. */
    {
        dw_buffer buffer = {NULL, 0};
        uint32_t status = call(ABI_VERSION, 1, valid_parse_trailing, (uint32_t)strlen(valid_parse_trailing), &buffer);
        check(status == 0, "accept_trailing_json_whitespace");
        if (status == 0) {
            check(body_contains(&buffer, "\"success\":true"), "accept_trailing_parse_success");
            check(body_contains(&buffer, "\"policyCount\":1"), "accept_trailing_policy_count");
        }
        release(&buffer);
    }
    {
        dw_buffer buffer = {NULL, 0};
        static const char leading[] = " \t\r\n{}";
        uint32_t status = call(ABI_VERSION, 0, leading, (uint32_t)strlen(leading), &buffer);
        check(status == 0, "accept_leading_json_whitespace");
        if (status == 0) {
            check(body_contains(&buffer, "\"abiVersion\":1"), "accept_leading_identity");
        }
        release(&buffer);
    }
}

static void test_parse_results(void)
{
    static const char *success = "{\"source\":\"permit(principal, action, resource);\"}";
    static const char *empty = "{\"source\":\"\"}";
    static const char *failure = "{\"source\":\"permit(\"}";
    static const char *unicode_bom = "{\"source\":\"\\ufeffpermit(\"}";
    dw_buffer buffer = {NULL, 0};
    uint32_t status;

    status = call(ABI_VERSION, 1, success, (uint32_t)strlen(success), &buffer);
    check(status == 0, "parse_success_status");
    if (status == 0) {
        check(buffer.len > 0 && buffer.len <= RESPONSE_LIMIT, "parse_success_bounded");
        check(body_contains(&buffer, "\"success\":true"), "parse_success_flag");
        check(body_contains(&buffer, "\"policyCount\":1"), "parse_success_count");
        check(body_contains(&buffer, "\"diagnostics\":[]"), "parse_success_diagnostics");
    }
    release(&buffer);

    status = call(ABI_VERSION, 1, empty, (uint32_t)strlen(empty), &buffer);
    check(status == 0, "parse_empty_status");
    if (status == 0) {
        check(body_contains(&buffer, "\"success\":true"), "parse_empty_success");
        check(body_contains(&buffer, "\"policyCount\":0"), "parse_empty_count");
    }
    release(&buffer);

    status = call(ABI_VERSION, 1, failure, (uint32_t)strlen(failure), &buffer);
    check(status == 0, "parse_failure_status_zero");
    if (status == 0) {
        check(body_contains(&buffer, "\"success\":false"), "parse_failure_flag");
        check(body_contains(&buffer, "\"policyCount\":null"), "parse_failure_count_null");
        check(body_contains(&buffer, "\"category\":\"syntax\""), "parse_failure_category");
        check(body_contains(&buffer, "\"location\":{\"offset\":7,\"length\":1}"), "parse_failure_eof_location");
    }
    release(&buffer);

    status = call(ABI_VERSION, 1, unicode_bom, (uint32_t)strlen(unicode_bom), &buffer);
    check(status == 0, "parse_bom_failure_status_zero");
    if (status == 0) {
        check(body_contains(&buffer, "\"location\":{\"offset\":10,\"length\":1}"), "parse_bom_location_rebased");
    }
    release(&buffer);
}

static void test_limits(void)
{
    uint8_t *source = (uint8_t *)malloc(SOURCE_LIMIT);
    size_t i;
    if (source == NULL) {
        fail("source_exactly_limit", "allocation failure");
        return;
    }
    source[0] = '/';
    source[1] = '/';
    memset(source + 2, 'a', SOURCE_LIMIT - 2);
    {
        size_t request_len = SOURCE_LIMIT + strlen("{\"source\":\"\"}");
        uint8_t *request = (uint8_t *)malloc(request_len);
        dw_buffer buffer = {NULL, 0};
        uint32_t status;
        if (request == NULL) {
            free(source);
            fail("source_exactly_limit", "allocation failure");
            return;
        }
        memcpy(request, "{\"source\":\"", 11);
        memcpy(request + 11, source, SOURCE_LIMIT);
        memcpy(request + 11 + SOURCE_LIMIT, "\"}", 2);
        status = call(ABI_VERSION, 1, request, (uint32_t)request_len, &buffer);
        check(status == 0, "source_exactly_limit_accepted");
        if (status == 0) {
            check(body_contains(&buffer, "\"success\":true"), "source_exactly_limit_success");
            check(body_contains(&buffer, "\"policyCount\":0"), "source_exactly_limit_count");
        }
        release(&buffer);
        free(request);
    }
    free(source);

    /* A JSON-escaped source grows six bytes per decoded byte; the decoded
     * ceiling must be enforced even though the request is under its ceiling. */
    {
        size_t decoded = SOURCE_LIMIT + 1u;
        size_t request_len = strlen("{\"source\":\"\"}") + 6u * decoded;
        uint8_t *request;
        dw_buffer buffer = {NULL, 0};
        uint32_t status;
        if (request_len > REQUEST_LIMIT) {
            fail("escaped_source_limit", "probe request exceeds documented ceiling");
            return;
        }
        request = (uint8_t *)malloc(request_len);
        if (request == NULL) {
            fail("escaped_source_limit", "allocation failure");
            return;
        }
        memcpy(request, "{\"source\":\"", 11);
        for (i = 0; i < decoded; i++) {
            memcpy(request + 11 + 6 * i, "\\u0041", 6);
        }
        memcpy(request + 11 + 6 * decoded, "\"}", 2);
        status = call(ABI_VERSION, 1, request, (uint32_t)(request_len), &buffer);
        check(status == 6, "escaped_source_over_limit_rejected");
        check(slot_empty(&buffer), "escaped_source_over_limit_empty");
        release(&buffer);
        free(request);
    }

    /* A 1 MiB malformed string token is echoed whole in the diagnostic
     * message. Control characters there expand sixfold in the JSON reply, so
     * the bridge must stop at the documented 2 MiB response ceiling. */
    {
        size_t decoded = SOURCE_LIMIT;
        size_t request_len = 17u + 6u * (decoded - 2u);
        uint8_t *request;
        dw_buffer buffer = {NULL, 0};
        uint32_t status;
        if (request_len > REQUEST_LIMIT) {
            fail("response_ceiling_rejected", "probe request exceeds documented ceiling");
            return;
        }
        request = (uint8_t *)malloc(request_len);
        if (request == NULL) {
            fail("response_ceiling_rejected", "allocation failure");
            return;
        }
        memcpy(request, "{\"source\":\"\\\"", 13);
        for (i = 0; i < decoded - 2u; i++) {
            memcpy(request + 13 + 6 * i, "\\u0001", 6);
        }
        memcpy(request + request_len - 4u, "\\\"\"}", 4);
        status = call(ABI_VERSION, 1, request, (uint32_t)request_len, &buffer);
        check(status == 6, "response_ceiling_rejected");
        check(slot_empty(&buffer), "response_ceiling_empty");
        release(&buffer);
        free(request);
    }

    /* The request ceiling itself is inclusive; one byte more is rejected
     * before UTF-8 or JSON validation. */
    {
        uint8_t *request = (uint8_t *)malloc(REQUEST_LIMIT);
        dw_buffer buffer = {NULL, 0};
        uint32_t status;
        if (request == NULL) {
            fail("request_exactly_limit", "allocation failure");
            return;
        }
        memset(request, ' ', REQUEST_LIMIT);
        request[0] = '{';
        request[REQUEST_LIMIT - 1] = '}';
        status = call(ABI_VERSION, 0, request, REQUEST_LIMIT, &buffer);
        check(status == 0, "request_exactly_limit_accepted");
        if (status == 0) {
            check(body_contains(&buffer, "\"abiVersion\":1"), "request_exactly_limit_identity");
        }
        release(&buffer);
        free(request);
    }
    {
        uint8_t *request = (uint8_t *)malloc((size_t)REQUEST_LIMIT + 1u);
        dw_buffer buffer = {NULL, 0};
        uint32_t status;
        if (request == NULL) {
            fail("request_over_limit", "allocation failure");
            return;
        }
        memset(request, ' ', (size_t)REQUEST_LIMIT + 1u);
        status = call(ABI_VERSION, 0, request, REQUEST_LIMIT + 1u, &buffer);
        check(status == 6, "request_over_limit_rejected");
        check(slot_empty(&buffer), "request_over_limit_empty");
        release(&buffer);
        free(request);
    }
}

static void test_ownership(void)
{
    dw_buffer buffer = {(uint8_t *)(uintptr_t)1, 0xdeadbeefu};
    uint32_t status;
    unsigned i;

    status = call(ABI_VERSION, 0, "{}", 2, &buffer);
    check(status == 0, "ownership_call_ok");
    check(buffer.data != NULL && buffer.len > 0, "ownership_buffer_nonempty");
    release(&buffer);
    check(slot_empty(&buffer), "ownership_free_resets");
    release(&buffer);
    check(slot_empty(&buffer), "ownership_double_free_safe");
    release(NULL);
    pass("ownership_free_null_safe");

    buffer.data = NULL;
    buffer.len = 0;
    release(&buffer);
    check(slot_empty(&buffer), "ownership_free_empty_slot_safe");

    for (i = 0; i < 1024; i++) {
        status = call(ABI_VERSION, 0, "{}", 2, &buffer);
        if (status != 0 || buffer.data == NULL || buffer.len == 0) {
            fail("ownership_repeated_lifecycle", "call failed during repetition");
            return;
        }
        release(&buffer);
        if (!slot_empty(&buffer)) {
            fail("ownership_repeated_lifecycle", "free did not reset during repetition");
            return;
        }
    }
    pass("ownership_repeated_lifecycle");
}

typedef struct {
    unsigned index;
    unsigned iterations;
    unsigned failures;
} thread_arg;

#if defined(_WIN32)
static DWORD WINAPI worker(LPVOID opaque)
#else
static void *worker(void *opaque)
#endif
{
    thread_arg *arg = (thread_arg *)opaque;
    static const char *success = "{\"source\":\"permit(principal, action, resource);\"}";
    static const char *failure = "{\"source\":\"permit(\"}";
    static const char *invalid = "{\"source\":\"a\",\"source\":\"b\"}";
    unsigned i;

    for (i = 0; i < arg->iterations; i++) {
        dw_buffer buffer = {NULL, 0};
        uint32_t status = call(ABI_VERSION, 0, "{}", 2, &buffer);
        if (status != 0 || buffer.data == NULL || !body_contains(&buffer, "\"abiVersion\":1")) {
            arg->failures++;
        }
        release(&buffer);

        status = call(ABI_VERSION, 1, success, (uint32_t)strlen(success), &buffer);
        if (status != 0 || !body_contains(&buffer, "\"success\":true")) {
            arg->failures++;
        }
        release(&buffer);

        status = call(ABI_VERSION, 1, failure, (uint32_t)strlen(failure), &buffer);
        if (status != 0 || !body_contains(&buffer, "\"success\":false")) {
            arg->failures++;
        }
        release(&buffer);

        status = call(ABI_VERSION, 1, invalid, (uint32_t)strlen(invalid), &buffer);
        if (status != 5 || !slot_empty(&buffer)) {
            arg->failures++;
        }
        release(&buffer);
    }
#if defined(_WIN32)
    return 0;
#else
    return NULL;
#endif
}

static void test_concurrency(void)
{
    enum { threads = 8, iterations = 64 };
    thread_arg args[threads];
    unsigned failures_total = 0;
    unsigned i;
#if defined(_WIN32)
    HANDLE handles[threads];
    for (i = 0; i < threads; i++) {
        args[i].index = i;
        args[i].iterations = iterations;
        args[i].failures = 0;
        handles[i] = CreateThread(NULL, 0, worker, &args[i], 0, NULL);
        if (handles[i] == NULL) {
            fail("concurrent_calls", "CreateThread failed");
            return;
        }
    }
    for (i = 0; i < threads; i++) {
        WaitForSingleObject(handles[i], INFINITE);
        CloseHandle(handles[i]);
    }
#else
    pthread_t handles[threads];
    for (i = 0; i < threads; i++) {
        args[i].index = i;
        args[i].iterations = iterations;
        args[i].failures = 0;
        if (pthread_create(&handles[i], NULL, worker, &args[i]) != 0) {
            fail("concurrent_calls", "pthread_create failed");
            return;
        }
    }
    for (i = 0; i < threads; i++) {
        pthread_join(handles[i], NULL);
    }
#endif
    for (i = 0; i < threads; i++) {
        failures_total += args[i].failures;
    }
    check(failures_total == 0, "concurrent_calls");
}

static int selftest(const char *library, const char *expected_target)
{
    uint32_t abi;
    printf("LAYOUT sizeof=%lu data=%lu len=%lu align=%lu\n",
           (unsigned long)sizeof(dw_buffer), (unsigned long)offsetof(dw_buffer, data),
           (unsigned long)offsetof(dw_buffer, len), (unsigned long)_Alignof(dw_buffer));
    load_library(library);
    abi = fn_abi_version();
    check(abi == ABI_VERSION, "abi_version_is_one");
    if (abi != ABI_VERSION) {
        printf("SUMMARY pass=%d fail=%d\n", 0, failures);
        return 1;
    }
    test_identity(expected_target);
    test_rejections();
    test_parse_results();
    test_limits();
    test_ownership();
    test_concurrency();
    printf("SUMMARY fail=%d\n", failures);
    printf(failures == 0 ? "RESULT OK\n" : "RESULT FAILED\n");
    return failures == 0 ? 0 : 1;
}

static int read_file(const char *path, uint8_t **bytes, uint32_t *length)
{
    FILE *file = fopen(path, "rb");
    long size;
    uint8_t *buffer;
    if (file == NULL) {
        fprintf(stderr, "cannot open %s\n", path);
        return 0;
    }
    if (fseek(file, 0, SEEK_END) != 0) {
        fclose(file);
        return 0;
    }
    size = ftell(file);
    if (size < 0 || (unsigned long)size > 0xffffffffu) {
        fclose(file);
        return 0;
    }
    rewind(file);
    buffer = (uint8_t *)malloc((size_t)size + 1);
    if (buffer == NULL) {
        fclose(file);
        return 0;
    }
    if ((size_t)size > 0 && fread(buffer, 1, (size_t)size, file) != (size_t)size) {
        free(buffer);
        fclose(file);
        return 0;
    }
    fclose(file);
    *bytes = buffer;
    *length = (uint32_t)size;
    return 1;
}

static void write_u32(FILE *file, uint32_t value)
{
    uint8_t bytes[4];
    bytes[0] = (uint8_t)(value & 0xff);
    bytes[1] = (uint8_t)((value >> 8) & 0xff);
    bytes[2] = (uint8_t)((value >> 16) & 0xff);
    bytes[3] = (uint8_t)((value >> 24) & 0xff);
    if (fwrite(bytes, 1, 4, file) != 4) {
        fprintf(stderr, "write failed\n");
        exit(2);
    }
}

static int probe(const char *library, const char *manifest_path, const char *results_path)
{
    FILE *manifest;
    FILE *results;
    char line[8192];
    uint32_t count = 0;
    load_library(library);
    manifest = fopen(manifest_path, "r");
    if (manifest == NULL) {
        fprintf(stderr, "cannot open manifest %s\n", manifest_path);
        return 2;
    }
    results = fopen(results_path, "wb+");
    if (results == NULL) {
        fclose(manifest);
        fprintf(stderr, "cannot create results %s\n", results_path);
        return 2;
    }
    fwrite("D1QP", 1, 4, results);
    write_u32(results, 1);
    write_u32(results, 0); /* patched */

    while (fgets(line, sizeof line, manifest) != NULL) {
        char *id;
        char *operation_text;
        char *path;
        char *newline;
        unsigned long operation;
        uint8_t *request = NULL;
        uint32_t request_len = 0;
        dw_buffer output = {NULL, 0};
        uint32_t status;
        uint32_t id_len;

        newline = strpbrk(line, "\r\n");
        if (newline != NULL) {
            *newline = '\0';
        }
        if (line[0] == '\0') {
            continue;
        }
        id = line;
        operation_text = strchr(id, '\t');
        if (operation_text == NULL) {
            fprintf(stderr, "bad manifest line: %s\n", id);
            fclose(manifest);
            fclose(results);
            return 2;
        }
        *operation_text++ = '\0';
        path = strchr(operation_text, '\t');
        if (path == NULL) {
            fprintf(stderr, "bad manifest line for %s\n", id);
            fclose(manifest);
            fclose(results);
            return 2;
        }
        *path++ = '\0';
        operation = strtoul(operation_text, NULL, 10);
        if (operation > 0xffffffffu) {
            operation = 0xffffffffu;
        }
        if (!read_file(path, &request, &request_len)) {
            fprintf(stderr, "cannot read request for %s: %s\n", id, path);
            fclose(manifest);
            fclose(results);
            return 2;
        }
        printf("CASE %s op=%lu len=%u\n", id, operation, request_len);
        fflush(stdout);
        status = call(ABI_VERSION, (uint32_t)operation, request, request_len, &output);
        id_len = (uint32_t)strlen(id);
        write_u32(results, id_len);
        if (fwrite(id, 1, id_len, results) != id_len) {
            fprintf(stderr, "write failed\n");
            exit(2);
        }
        write_u32(results, status);
        write_u32(results, status == 0 ? output.len : 0);
        if (status == 0 && output.len > 0 && fwrite(output.data, 1, output.len, results) != output.len) {
            fprintf(stderr, "write failed\n");
            exit(2);
        }
        write_u32(results, slot_empty(&output) ? 1u : 0u);
        release(&output);
        free(request);
        count++;
    }
    fclose(manifest);
    if (fseek(results, 8, SEEK_SET) != 0) {
        fclose(results);
        return 2;
    }
    write_u32(results, count);
    if (fclose(results) != 0) {
        return 2;
    }
    printf("PROBED cases=%u\n", count);
    return 0;
}

int main(int argc, char **argv)
{
    if (argc >= 3 && strcmp(argv[1], "selftest") == 0) {
        return selftest(argv[2], argc >= 4 ? argv[3] : NULL);
    }
    if (argc >= 5 && strcmp(argv[1], "probe") == 0) {
        return probe(argv[2], argv[3], argv[4]);
    }
    fprintf(stderr, "usage: %s selftest <library> [target]\n       %s probe <library> <manifest.tsv> <results.bin>\n",
            argv[0], argv[0]);
    return 2;
}
