//! DogwoodSharp ABI v1. See docs/architecture/native-interop.md.
#![deny(unsafe_op_in_unsafe_fn)]

use dogwood_language::{Error, ParsedPolicySet, ServiceSchema, SourceLoc};
use serde::{Deserialize, Serialize};
use std::io::{self, Write};
use std::panic::catch_unwind;
use std::{ptr, slice, str};

const ABI: u32 = 1;
const SOURCE_LIMIT: usize = 1024 * 1024;
const REQUEST_LIMIT: usize = 6 * SOURCE_LIMIT + 64;
const RESPONSE_LIMIT: usize = 2 * SOURCE_LIMIT;

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
#[repr(u32)]
enum Status {
    Ok = 0,
    InvalidArgument = 1,
    UnsupportedAbi = 2,
    UnsupportedOperation = 3,
    InvalidUtf8 = 4,
    InvalidEnvelope = 5,
    Limit = 6,
    Panic = 7,
    InternalError = 8,
}

/// Native-owned output. The length is authoritative.
#[repr(C)]
pub struct Buffer {
    data: *mut u8,
    len: u32,
}

impl Buffer {
    const EMPTY: Self = Self {
        data: ptr::null_mut(),
        len: 0,
    };
}

/// Discover the ABI supported by this library.
#[unsafe(no_mangle)]
pub extern "C" fn dogwoodsharp_abi_version() -> u32 {
    ABI
}

/// Run an ABI v1 operation. A nonzero status leaves an empty output.
///
/// # Safety
/// A non-null output must be valid and aligned for a Buffer write and must not
/// hold a live allocation. For accepted lengths, non-null input must address
/// readable, immutable bytes for the call. The ranges must not overlap.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn dogwoodsharp_call_v1(
    abi: u32,
    operation: u32,
    input: *const u8,
    input_len: u32,
    output: *mut Buffer,
) -> u32 {
    if output.is_null() {
        return Status::InvalidArgument as u32;
    }
    unsafe { output.write(Buffer::EMPTY) };
    let result = contain(|| {
        #[cfg(test)]
        TEST_PANIC.with(|panic| assert!(!panic.replace(false), "injected test panic"));
        if abi != ABI {
            return Err(Status::UnsupportedAbi);
        }
        if operation > 1 {
            return Err(Status::UnsupportedOperation);
        }
        let len = input_len as usize;
        if len > REQUEST_LIMIT {
            return Err(Status::Limit);
        }
        if len == 0 || input.is_null() {
            return Err(Status::InvalidArgument);
        }
        let bytes = unsafe { slice::from_raw_parts(input, len) };
        let json = str::from_utf8(bytes).map_err(|_| Status::InvalidUtf8)?;
        dispatch(operation, json)
    });
    match result {
        Ok(bytes) => {
            let len = bytes.len() as u32;
            let data = Box::into_raw(bytes).cast::<u8>();
            unsafe { output.write(Buffer { data, len }) };
            Status::Ok as u32
        }
        Err(status) => status as u32,
    }
}

fn contain<T>(
    operation: impl FnOnce() -> Result<T, Status> + std::panic::UnwindSafe,
) -> Result<T, Status> {
    match catch_unwind(operation) {
        Ok(result) => result,
        Err(payload) => {
            // A panic payload may itself panic on drop. Keep that unwind inside the boundary.
            if let Err(nested) = catch_unwind(std::panic::AssertUnwindSafe(|| drop(payload))) {
                std::mem::forget(nested);
            }
            Err(Status::Panic)
        }
    }
}

/// Free a buffer created by this library and reset its slot. Null is a no-op.
///
/// # Safety
/// A non-null slot must be aligned and writable and contain either an empty
/// buffer or the unchanged, uniquely owned result of this library's call.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn dogwoodsharp_free_v1(output: *mut Buffer) {
    if !output.is_null() {
        let buffer = unsafe { output.replace(Buffer::EMPTY) };
        if !buffer.data.is_null() {
            let allocation = ptr::slice_from_raw_parts_mut(buffer.data, buffer.len as usize);
            unsafe { drop(Box::from_raw(allocation)) };
        }
    }
}

#[derive(Deserialize)]
#[serde(deny_unknown_fields)]
struct VersionRequest {}

#[derive(Deserialize)]
#[serde(deny_unknown_fields)]
struct ParseRequest {
    source: String,
}

fn dispatch(operation: u32, json: &str) -> Result<Box<[u8]>, Status> {
    if !json
        .trim_start_matches([' ', '\t', '\n', '\r'])
        .starts_with('{')
    {
        return Err(Status::InvalidEnvelope);
    }
    match operation {
        0 => {
            serde_json::from_str::<VersionRequest>(json).map_err(|_| Status::InvalidEnvelope)?;
            encode(&serde_json::json!({
                "abiVersion": ABI,
                "bridgeVersion": env!("CARGO_PKG_VERSION"),
                "dogwoodRevision": "929f49e7305534ac61ef06cf8e47269c596dc851",
                "dogwoodVersion": "1.1.2",
                "cedarVersion": "4.11.0",
                "rustVersion": "1.99.1",
                "target": env!("DOGWOODSHARP_TARGET"),
                "dogwoodFeatures": [],
                "cedarFeatures": ["datetime", "decimal", "ipaddr"]
            }))
        }
        1 => {
            let request: ParseRequest =
                serde_json::from_str(json).map_err(|_| Status::InvalidEnvelope)?;
            if request.source.len() > SOURCE_LIMIT {
                return Err(Status::Limit);
            }
            parse(&request.source)
        }
        _ => Err(Status::UnsupportedOperation),
    }
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct ParseReply {
    success: bool,
    policy_count: Option<usize>,
    diagnostics: Vec<Diagnostic>,
}

#[derive(Serialize)]
struct Diagnostic {
    category: &'static str,
    message: String,
    location: Option<Location>,
}

#[derive(Serialize)]
struct Location {
    offset: usize,
    length: usize,
}

fn diagnostic(
    category: &'static str,
    message: String,
    loc: &SourceLoc,
    source: &str,
) -> Diagnostic {
    let span = loc.span();
    // The pinned parser computes spans after stripping one leading BOM, while
    // SourceLoc retains the original source. Restore the original coordinates.
    let offset = span.offset() + if source.starts_with('\u{feff}') { 3 } else { 0 };
    Diagnostic {
        category,
        message,
        location: (loc.source() == source
            && offset <= source.len()
            && (span.len() <= source.len() - offset
                || (offset == source.len() && span.len() == 1)))
            .then_some(Location {
                offset,
                length: span.len(),
            }),
    }
}

fn parse(source: &str) -> Result<Box<[u8]>, Status> {
    let result = ParsedPolicySet::parse(source, &ServiceSchema::defaults());
    let reply = match result {
        Ok(policies) => ParseReply {
            success: true,
            policy_count: Some(policies.policy_count()),
            diagnostics: Vec::new(),
        },
        Err(error) => {
            let diagnostics = match error {
                Error::Parse(errors) => errors
                    .iter()
                    .map(|e| diagnostic("syntax", e.to_string(), e.location(), source))
                    .collect(),
                Error::Macro(e) => vec![diagnostic("macro", e.to_string(), e.location(), source)],
                _ => return Err(Status::InternalError),
            };
            ParseReply {
                success: false,
                policy_count: None,
                diagnostics,
            }
        }
    };
    encode(&reply)
}

#[derive(Default)]
struct BoundedOutput {
    bytes: Vec<u8>,
    exceeded: bool,
}

impl Write for BoundedOutput {
    fn write(&mut self, bytes: &[u8]) -> io::Result<usize> {
        if bytes.len() > RESPONSE_LIMIT - self.bytes.len() {
            self.exceeded = true;
            return Err(io::Error::other("response limit exceeded"));
        }
        self.bytes.extend_from_slice(bytes);
        Ok(bytes.len())
    }

    fn flush(&mut self) -> io::Result<()> {
        Ok(())
    }
}

fn encode(value: &impl Serialize) -> Result<Box<[u8]>, Status> {
    let mut output = BoundedOutput::default();
    serde_json::to_writer(&mut output, value).map_err(|_| {
        if output.exceeded {
            Status::Limit
        } else {
            Status::InternalError
        }
    })?;
    Ok(output.bytes.into_boxed_slice())
}

#[cfg(test)]
thread_local! {
    static TEST_PANIC: std::cell::Cell<bool> = const { std::cell::Cell::new(false) };
}

#[cfg(test)]
mod tests;
