//! Independent E1 differential reference over the pinned Cedar SDK.
//!
//! ```text
//! dogwoodsharp-e1-ref <operation> <input.json>    run one FFI operation, print the raw answer
//! ```
//!
//! Operation numbers mirror the CedarSharp native ABI so a managed test can
//! drive both engines with identical envelopes. The tool interprets nothing:
//! it prints the raw upstream FFI answer string and exits zero. A rejected
//! input envelope exits nonzero with the reason on stderr, mirroring the
//! bridge's failure status. The pinned SDK is the bridge's Cedar version, so
//! the comparison isolates the SDK version pair rather than any wrapper.

use std::path::PathBuf;
use std::process::ExitCode;

const OPERATION_VERSION: u32 = 0;
const OPERATION_AUTHORIZE: u32 = 1;
const OPERATION_VALIDATE: u32 = 2;
const OPERATION_CHECK_POLICIES: u32 = 3;
const OPERATION_CHECK_SCHEMA: u32 = 4;
const OPERATION_CHECK_ENTITIES: u32 = 5;
const OPERATION_CHECK_CONTEXT: u32 = 6;

fn main() -> ExitCode {
    let arguments: Vec<String> = std::env::args().skip(1).collect();
    let (operation, path) = match arguments.as_slice() {
        [operation, path] => match operation.parse::<u32>() {
            Ok(operation) => (operation, PathBuf::from(path)),
            Err(_) => {
                eprintln!("usage: dogwoodsharp-e1-ref <operation 0..6> <input.json>");
                return ExitCode::from(2);
            }
        },
        _ => {
            eprintln!("usage: dogwoodsharp-e1-ref <operation 0..6> <input.json>");
            return ExitCode::from(2);
        }
    };
    let input = match std::fs::read_to_string(&path) {
        Ok(text) => text,
        Err(error) => {
            eprintln!("cannot read {}: {error}", path.display());
            return ExitCode::from(2);
        }
    };
    let answer = match operation {
        OPERATION_VERSION => {
            println!(
                "{{\"sdkVersion\":\"{}\",\"languageVersion\":\"{}\"}}",
                cedar_policy::ffi::get_sdk_version(),
                cedar_policy::ffi::get_lang_version()
            );
            return ExitCode::SUCCESS;
        }
        OPERATION_AUTHORIZE => cedar_policy::ffi::is_authorized_json_str(&input),
        OPERATION_VALIDATE => cedar_policy::ffi::validate_json_str(&input),
        OPERATION_CHECK_POLICIES => cedar_policy::ffi::check_parse_policy_set_json_str(&input),
        OPERATION_CHECK_SCHEMA => cedar_policy::ffi::check_parse_schema_json_str(&input),
        OPERATION_CHECK_ENTITIES => cedar_policy::ffi::check_parse_entities_json_str(&input),
        OPERATION_CHECK_CONTEXT => cedar_policy::ffi::check_parse_context_json_str(&input),
        _ => {
            eprintln!("unsupported operation {operation}");
            return ExitCode::from(2);
        }
    };
    match answer {
        Ok(text) => {
            println!("{text}");
            ExitCode::SUCCESS
        }
        Err(error) => {
            eprintln!("operation {operation} rejected its input envelope: {error}");
            ExitCode::from(1)
        }
    }
}
