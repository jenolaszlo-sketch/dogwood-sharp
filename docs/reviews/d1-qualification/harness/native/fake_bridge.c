/*
 * Test-only fake DogwoodSharp native bridge for qualification. It implements
 * the documented C ABI but returns controllable responses selected by
 * environment variables. It is never shipped and is not used by production CI.
 *
 *   D1_FAKE_ABI          abi_version() result (default 1)
 *   D1_FAKE_VERSION      operation 0 response variant (default ok)
 *   D1_FAKE_PARSE        operation 1 response variant (default success)
 *   D1_FAKE_STATUS       status returned by every call, leaving output empty
 *   D1_FAKE_PARSE_STATUS status returned only by operation 1
 *   D1_FAKE_TARGET       target field emitted by the ok identity response
 *   D1_FAKE_MARK         file that receives one "C:<op>" or "F" line per call
 */

#if defined(_WIN32)
#define _CRT_SECURE_NO_WARNINGS
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#define EXPORT __declspec(dllexport)
#else
#define EXPORT __attribute__((visibility("default")))
#endif

#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

typedef struct {
    uint8_t *data;
    uint32_t len;
} dw_buffer;

#define RESPONSE_LIMIT (2u * 1024u * 1024u)

static const char *env_or(const char *name, const char *fallback)
{
    const char *value = getenv(name);
    return value != NULL && value[0] != '\0' ? value : fallback;
}

static void mark(const char *line)
{
    const char *path = getenv("D1_FAKE_MARK");
    FILE *file;
    if (path == NULL || path[0] == '\0') {
        return;
    }
    file = fopen(path, "a");
    if (file != NULL) {
        fprintf(file, "%s\n", line);
        fclose(file);
    }
}

static void mark_call(uint32_t operation)
{
    char line[32];
    snprintf(line, sizeof line, "C:%u", operation);
    mark(line);
}

static uint8_t *copy_bytes(const void *source, size_t length)
{
    uint8_t *bytes = (uint8_t *)malloc(length == 0 ? 1 : length);
    if (bytes != NULL && length > 0) {
        memcpy(bytes, source, length);
    }
    return bytes;
}

static uint32_t reply(dw_buffer *output, const char *json)
{
    size_t length = strlen(json);
    uint8_t *bytes = copy_bytes(json, length);
    if (bytes == NULL) {
        return 8;
    }
    output->data = bytes;
    output->len = (uint32_t)length;
    return 0;
}

static uint32_t reply_bytes(dw_buffer *output, const uint8_t *bytes, uint32_t length)
{
    uint8_t *copy = copy_bytes(bytes, length);
    if (copy == NULL) {
        return 8;
    }
    output->data = copy;
    output->len = length;
    return 0;
}

EXPORT uint32_t dogwoodsharp_abi_version(void)
{
    const char *value = getenv("D1_FAKE_ABI");
    return value != NULL && value[0] != '\0' ? (uint32_t)strtoul(value, NULL, 10) : 1u;
}

static const char *version_json(char *buffer, size_t size)
{
    const char *variant = env_or("D1_FAKE_VERSION", "ok");
    const char *target = env_or("D1_FAKE_TARGET", "x86_64-unknown-linux-gnu");
    const char *bridge = "0.1.0";
    const char *revision = "929f49e7305534ac61ef06cf8e47269c596dc851";
    const char *dogwood = "1.1.2";
    const char *cedar = "4.11.0";
    const char *rust = "1.99.1";
    const char *abi = "1";
    const char *dogwood_features = "[]";
    const char *cedar_features = "[\"datetime\",\"decimal\",\"ipaddr\"]";
    int include_rust = 1;
    int duplicate_target = 0;
    const char *extra = "";
    char rust_property[64];

    if (strcmp(variant, "wrong-target") == 0) {
        target = "other-target";
    } else if (strcmp(variant, "wrong-abi") == 0) {
        abi = "2";
    } else if (strcmp(variant, "abi-string") == 0) {
        abi = "\"1\"";
    } else if (strcmp(variant, "wrong-bridge") == 0) {
        bridge = "9.9.9";
    } else if (strcmp(variant, "wrong-revision") == 0) {
        revision = "0000000000000000000000000000000000000000";
    } else if (strcmp(variant, "wrong-dogwood") == 0) {
        dogwood = "0.0.0";
    } else if (strcmp(variant, "wrong-cedar") == 0) {
        cedar = "0.0.0";
    } else if (strcmp(variant, "wrong-rust") == 0) {
        rust = "0.0.0";
    } else if (strcmp(variant, "features-order") == 0) {
        cedar_features = "[\"ipaddr\",\"decimal\",\"datetime\"]";
    } else if (strcmp(variant, "features-extra") == 0) {
        cedar_features = "[\"datetime\",\"decimal\",\"ipaddr\",\"extra\"]";
    } else if (strcmp(variant, "features-null") == 0) {
        cedar_features = "null";
    } else if (strcmp(variant, "dogwood-features") == 0) {
        dogwood_features = "[\"extra\"]";
    } else if (strcmp(variant, "missing") == 0) {
        include_rust = 0;
    } else if (strcmp(variant, "duplicate") == 0) {
        duplicate_target = 1;
    } else if (strcmp(variant, "extra") == 0) {
        extra = ",\"extra\":1";
    } else if (strcmp(variant, "empty-object") == 0) {
        return "{}";
    } else if (strcmp(variant, "invalid-json") == 0) {
        return "{";
    } else if (strcmp(variant, "top-array") == 0) {
        return "[]";
    }

    snprintf(rust_property, sizeof rust_property, "\"rustVersion\":\"%s\",", rust);
    snprintf(buffer, size,
             "{"
             "\"bridgeVersion\":\"%s\","
             "\"target\":\"%s\",%s"
             "\"abiVersion\":%s,"
             "\"dogwoodRevision\":\"%s\","
             "\"dogwoodVersion\":\"%s\","
             "\"cedarVersion\":\"%s\",%s"
             "\"dogwoodFeatures\":%s,"
             "\"cedarFeatures\":%s%s"
             "}",
             bridge, target, duplicate_target ? "" : "", abi, revision, dogwood, cedar,
             include_rust ? rust_property : "", dogwood_features, cedar_features, extra);
    /* The duplicate variant needs a second target property in a valid spot. */
    if (duplicate_target) {
        snprintf(buffer, size,
                 "{"
                 "\"target\":\"%s\","
                 "\"bridgeVersion\":\"0.1.0\","
                 "\"target\":\"%s\","
                 "\"abiVersion\":1,"
                 "\"dogwoodRevision\":\"929f49e7305534ac61ef06cf8e47269c596dc851\","
                 "\"dogwoodVersion\":\"1.1.2\","
                 "\"cedarVersion\":\"4.11.0\","
                 "\"rustVersion\":\"1.99.1\","
                 "\"dogwoodFeatures\":[],"
                 "\"cedarFeatures\":[\"datetime\",\"decimal\",\"ipaddr\"]"
                 "}",
                 target, target);
    }
    return buffer;
}

static const char *parse_json(void)
{
    const char *variant = env_or("D1_FAKE_PARSE", "success");
    if (strcmp(variant, "success-zero") == 0) {
        return "{\"success\":true,\"policyCount\":0,\"diagnostics\":[]}";
    }
    if (strcmp(variant, "success") == 0) {
        return "{\"success\":true,\"policyCount\":1,\"diagnostics\":[]}";
    }
    if (strcmp(variant, "failure") == 0) {
        return "{\"success\":false,\"policyCount\":null,\"diagnostics\":["
               "{\"category\":\"syntax\",\"message\":\"fake failure\",\"location\":{\"offset\":0,\"length\":5}}]}";
    }
    if (strcmp(variant, "failure-null-location") == 0) {
        return "{\"success\":false,\"policyCount\":null,\"diagnostics\":["
               "{\"category\":\"syntax\",\"message\":\"fake failure\",\"location\":null}]}";
    }
    if (strcmp(variant, "failure-eof") == 0) {
        return "{\"success\":false,\"policyCount\":null,\"diagnostics\":["
               "{\"category\":\"syntax\",\"message\":\"fake eof\",\"location\":{\"offset\":7,\"length\":1}}]}";
    }
    if (strcmp(variant, "success-extra-field") == 0) {
        return "{\"success\":true,\"policyCount\":1,\"diagnostics\":[],\"extra\":1}";
    }
    if (strcmp(variant, "success-missing-count") == 0) {
        return "{\"success\":true,\"diagnostics\":[]}";
    }
    if (strcmp(variant, "success-count-null") == 0) {
        return "{\"success\":true,\"policyCount\":null,\"diagnostics\":[]}";
    }
    if (strcmp(variant, "success-negative-count") == 0) {
        return "{\"success\":true,\"policyCount\":-1,\"diagnostics\":[]}";
    }
    if (strcmp(variant, "success-count-string") == 0) {
        return "{\"success\":true,\"policyCount\":\"1\",\"diagnostics\":[]}";
    }
    if (strcmp(variant, "success-count-overflow") == 0) {
        return "{\"success\":true,\"policyCount\":2147483648,\"diagnostics\":[]}";
    }
    if (strcmp(variant, "success-with-diagnostics") == 0) {
        return "{\"success\":true,\"policyCount\":1,\"diagnostics\":["
               "{\"category\":\"syntax\",\"message\":\"x\",\"location\":null}]}";
    }
    if (strcmp(variant, "failure-count-zero") == 0) {
        return "{\"success\":false,\"policyCount\":0,\"diagnostics\":["
               "{\"category\":\"syntax\",\"message\":\"x\",\"location\":null}]}";
    }
    if (strcmp(variant, "failure-empty-diagnostics") == 0) {
        return "{\"success\":false,\"policyCount\":null,\"diagnostics\":[]}";
    }
    if (strcmp(variant, "failure-unknown-category") == 0) {
        return "{\"success\":false,\"policyCount\":null,\"diagnostics\":["
               "{\"category\":\"other\",\"message\":\"x\",\"location\":null}]}";
    }
    if (strcmp(variant, "failure-location-string") == 0) {
        return "{\"success\":false,\"policyCount\":null,\"diagnostics\":["
               "{\"category\":\"syntax\",\"message\":\"x\",\"location\":\"x\"}]}";
    }
    if (strcmp(variant, "failure-location-negative") == 0) {
        return "{\"success\":false,\"policyCount\":null,\"diagnostics\":["
               "{\"category\":\"syntax\",\"message\":\"x\",\"location\":{\"offset\":-1,\"length\":1}}]}";
    }
    if (strcmp(variant, "failure-location-past-eof") == 0) {
        return "{\"success\":false,\"policyCount\":null,\"diagnostics\":["
               "{\"category\":\"syntax\",\"message\":\"x\",\"location\":{\"offset\":37,\"length\":0}}]}";
    }
    if (strcmp(variant, "failure-location-length-past-eof") == 0) {
        return "{\"success\":false,\"policyCount\":null,\"diagnostics\":["
               "{\"category\":\"syntax\",\"message\":\"x\",\"location\":{\"offset\":35,\"length\":2}}]}";
    }
    if (strcmp(variant, "failure-location-eof-length-two") == 0) {
        return "{\"success\":false,\"policyCount\":null,\"diagnostics\":["
               "{\"category\":\"syntax\",\"message\":\"x\",\"location\":{\"offset\":36,\"length\":2}}]}";
    }
    if (strcmp(variant, "failure-location-extra") == 0) {
        return "{\"success\":false,\"policyCount\":null,\"diagnostics\":["
               "{\"category\":\"syntax\",\"message\":\"x\",\"location\":{\"offset\":0,\"length\":1,\"x\":1}}]}";
    }
    if (strcmp(variant, "failure-location-missing") == 0) {
        return "{\"success\":false,\"policyCount\":null,\"diagnostics\":["
               "{\"category\":\"syntax\",\"message\":\"x\",\"location\":{\"offset\":0}}]}";
    }
    if (strcmp(variant, "failure-message-null") == 0) {
        return "{\"success\":false,\"policyCount\":null,\"diagnostics\":["
               "{\"category\":\"syntax\",\"message\":null,\"location\":null}]}";
    }
    if (strcmp(variant, "failure-category-number") == 0) {
        return "{\"success\":false,\"policyCount\":null,\"diagnostics\":["
               "{\"category\":1,\"message\":\"x\",\"location\":null}]}";
    }
    if (strcmp(variant, "failure-extra-field") == 0) {
        return "{\"success\":false,\"policyCount\":null,\"diagnostics\":["
               "{\"category\":\"syntax\",\"message\":\"x\",\"location\":null}],\"extra\":1}";
    }
    if (strcmp(variant, "failure-diagnostic-extra") == 0) {
        return "{\"success\":false,\"policyCount\":null,\"diagnostics\":["
               "{\"category\":\"syntax\",\"message\":\"x\",\"location\":null,\"extra\":1}]}";
    }
    if (strcmp(variant, "failure-missing-location") == 0) {
        return "{\"success\":false,\"policyCount\":null,\"diagnostics\":["
               "{\"category\":\"syntax\",\"message\":\"x\"}]}";
    }
    if (strcmp(variant, "duplicate-property") == 0) {
        return "{\"success\":true,\"success\":true,\"policyCount\":1,\"diagnostics\":[]}";
    }
    if (strcmp(variant, "invalid-json") == 0) {
        return "{";
    }
    if (strcmp(variant, "top-array") == 0) {
        return "[]";
    }
    if (strcmp(variant, "top-null") == 0) {
        return "null";
    }
    return "{\"success\":true,\"policyCount\":1,\"diagnostics\":[]}";
}

EXPORT uint32_t dogwoodsharp_call_v1(uint32_t abi, uint32_t operation, const uint8_t *input,
                                     uint32_t input_len, dw_buffer *output)
{
    const char *status_text;
    uint32_t status;
    (void)input;
    (void)input_len;
    if (output == NULL) {
        return 1;
    }
    output->data = NULL;
    output->len = 0;
    if (abi != 1) {
        return 2;
    }
    mark_call(operation);
    status_text = getenv("D1_FAKE_STATUS");
    if (status_text != NULL && status_text[0] != '\0') {
        return (uint32_t)strtoul(status_text, NULL, 10);
    }
    if (operation == 1) {
        status_text = getenv("D1_FAKE_PARSE_STATUS");
        if (status_text != NULL && status_text[0] != '\0') {
            return (uint32_t)strtoul(status_text, NULL, 10);
        }
    }
    if (operation == 0) {
        const char *variant = env_or("D1_FAKE_VERSION", "ok");
        char version[4096];
        if (strcmp(variant, "null-data") == 0) {
            output->data = NULL;
            output->len = 5;
            return 0;
        }
        if (strcmp(variant, "zero-length") == 0) {
            output->data = (uint8_t *)malloc(1);
            output->len = 0;
            return output->data == NULL ? 8 : 0;
        }
        if (strcmp(variant, "invalid-utf8") == 0) {
            static const uint8_t bad[] = {'{', 'x', ':', 0xff, '}'};
            return reply_bytes(output, bad, (uint32_t)sizeof bad);
        }
        if (strcmp(variant, "oversize") == 0) {
            uint32_t length = RESPONSE_LIMIT + 1u;
            uint8_t *body = (uint8_t *)malloc(length);
            if (body == NULL) {
                return 8;
            }
            memset(body, 'x', length);
            output->data = body;
            output->len = length;
            return 0;
        }
        return reply(output, version_json(version, sizeof version));
    }
    if (operation == 1) {
        const char *variant = env_or("D1_FAKE_PARSE", "success");
        if (strcmp(variant, "null-data") == 0) {
            output->data = NULL;
            output->len = 5;
            return 0;
        }
        if (strcmp(variant, "zero-length") == 0) {
            output->data = (uint8_t *)malloc(1);
            output->len = 0;
            return output->data == NULL ? 8 : 0;
        }
        if (strcmp(variant, "empty") == 0) {
            output->data = (uint8_t *)malloc(1);
            output->len = 0;
            return output->data == NULL ? 8 : 0;
        }
        if (strcmp(variant, "invalid-utf8") == 0) {
            static const uint8_t bad[] = {'{', 'x', ':', 0xff, '}'};
            return reply_bytes(output, bad, (uint32_t)sizeof bad);
        }
        if (strcmp(variant, "oversize") == 0) {
            uint32_t length = RESPONSE_LIMIT + 1u;
            uint8_t *body = (uint8_t *)malloc(length);
            if (body == NULL) {
                return 8;
            }
            memset(body, 'x', length);
            output->data = body;
            output->len = length;
            return 0;
        }
        return reply(output, parse_json());
    }
    status = 3;
    return status;
}

EXPORT void dogwoodsharp_free_v1(dw_buffer *output)
{
    mark("F");
    if (output != NULL) {
        uint8_t *data = output->data;
        output->data = NULL;
        output->len = 0;
        free(data);
    }
}
