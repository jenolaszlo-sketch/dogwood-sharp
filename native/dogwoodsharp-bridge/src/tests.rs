use super::*;
use serde_json::{Value, json};

fn call(abi: u32, op: u32, input: &[u8], buffer: &mut Buffer) -> u32 {
    unsafe { dogwoodsharp_call_v1(abi, op, input.as_ptr(), input.len() as u32, buffer) }
}

fn read(buffer: &Buffer) -> Value {
    assert!(!buffer.data.is_null());
    assert!((1..=RESPONSE_LIMIT).contains(&(buffer.len as usize)));
    let bytes = unsafe { slice::from_raw_parts(buffer.data, buffer.len as usize) };
    serde_json::from_slice(bytes).unwrap()
}

fn release(buffer: &mut Buffer) {
    unsafe { dogwoodsharp_free_v1(buffer) };
    assert!(buffer.data.is_null());
    assert_eq!(buffer.len, 0);
}

#[test]
fn version_and_repeated_output_lifecycle() {
    assert_eq!(dogwoodsharp_abi_version(), 1);
    let mut buffer = Buffer::EMPTY;
    for _ in 0..128 {
        assert_eq!(call(ABI, 0, b"{}", &mut buffer), 0);
        let version = read(&buffer);
        assert_eq!(version["abiVersion"], ABI);
        assert_eq!(version["target"], env!("DOGWOODSHARP_TARGET"));
        assert_eq!(version["dogwoodFeatures"], json!([]));
        release(&mut buffer);
        release(&mut buffer);
    }
    unsafe { dogwoodsharp_free_v1(ptr::null_mut()) };
}

#[test]
fn invalid_input_leaves_empty_output() {
    let mut buffer = Buffer::EMPTY;
    let cases: &[(u32, u32, &[u8], Status)] = &[
        (99, 0, b"{}", Status::UnsupportedAbi),
        (1, 99, b"{}", Status::UnsupportedOperation),
        (1, 1, b"", Status::InvalidArgument),
        (1, 1, b"\xff", Status::InvalidUtf8),
        (1, 1, b"{", Status::InvalidEnvelope),
        (1, 1, b"[]", Status::InvalidEnvelope),
        (1, 1, b"[\"source\"]", Status::InvalidEnvelope),
        (1, 0, b"[]", Status::InvalidEnvelope),
        (1, 0, br#"{"unexpected":true}"#, Status::InvalidEnvelope),
        (1, 1, b"{}", Status::InvalidEnvelope),
        (1, 1, br#"{"source":null}"#, Status::InvalidEnvelope),
        (
            1,
            1,
            br#"{"source":"","source":"duplicate"}"#,
            Status::InvalidEnvelope,
        ),
        (
            1,
            1,
            br#"{"source":"","unknown":true}"#,
            Status::InvalidEnvelope,
        ),
        (1, 1, br#"{"source":"\ud800"}"#, Status::InvalidEnvelope),
        (1, 1, br#"{"source":""} {}"#, Status::InvalidEnvelope),
    ];
    for &(abi, op, input, status) in cases {
        assert_eq!(call(abi, op, input, &mut buffer), status as u32);
        release(&mut buffer);
    }

    let status = unsafe { dogwoodsharp_call_v1(ABI, 1, ptr::null(), 1, &mut buffer) };
    assert_eq!(status, Status::InvalidArgument as u32);
    release(&mut buffer);
    let status = unsafe { dogwoodsharp_call_v1(ABI, 0, b"{}".as_ptr(), 2, ptr::null_mut()) };
    assert_eq!(status, Status::InvalidArgument as u32);
}

#[test]
fn request_source_and_response_limits() {
    let mut buffer = Buffer::EMPTY;
    let input = vec![b' '; REQUEST_LIMIT + 1];
    assert_eq!(call(ABI, 1, &input, &mut buffer), Status::Limit as u32);
    release(&mut buffer);

    let input = json!({"source": " ".repeat(SOURCE_LIMIT + 1)}).to_string();
    assert_eq!(
        call(ABI, 1, input.as_bytes(), &mut buffer),
        Status::Limit as u32
    );
    release(&mut buffer);

    let input = json!({"source": " ".repeat(SOURCE_LIMIT)}).to_string();
    assert_eq!(call(ABI, 1, input.as_bytes(), &mut buffer), 0);
    assert_eq!(read(&buffer)["policyCount"], 0);
    release(&mut buffer);

    assert_eq!(encode(&" ".repeat(RESPONSE_LIMIT)), Err(Status::Limit));
}

#[test]
fn unwind_returns_panic_status_without_disabling_subsequent_calls() {
    let mut buffer = Buffer::EMPTY;
    TEST_PANIC.with(|panic| panic.set(true));
    assert_eq!(call(ABI, 0, b"{}", &mut buffer), Status::Panic as u32);
    release(&mut buffer);
    assert_eq!(call(ABI, 0, b"{}", &mut buffer), 0);
    release(&mut buffer);
}

#[test]
fn differential_against_pinned_parser() {
    let manifest: Value = serde_json::from_str(include_str!(
        "../../../tests/DogwoodSharp.Conformance.Tests/parsing-v1.json"
    ))
    .unwrap();
    assert_eq!(manifest["fixtureVersion"], 1);
    assert_eq!(
        manifest["dogwoodRevision"],
        "929f49e7305534ac61ef06cf8e47269c596dc851"
    );
    let schema = ServiceSchema::defaults();

    for case in manifest["cases"].as_array().unwrap() {
        let id = case["id"].as_str().unwrap();
        let source = case["source"].as_str().unwrap();
        let input = json!({"source": source}).to_string();
        let mut buffer = Buffer::EMPTY;
        assert_eq!(call(ABI, 1, input.as_bytes(), &mut buffer), 0, "{id}");
        let reply = read(&buffer);
        release(&mut buffer);

        match ParsedPolicySet::parse(source, &schema) {
            Ok(policies) => {
                assert_eq!(reply["success"], true, "{id}");
                assert_eq!(reply["policyCount"], policies.policy_count(), "{id}");
                assert_eq!(reply["policyCount"], case["policyCount"], "{id}");
                assert_eq!(reply["diagnostics"], json!([]), "{id}");
            }
            Err(error) => {
                assert_eq!(reply["success"], false, "{id}");
                assert!(reply["policyCount"].is_null(), "{id}");
                let diagnostics = reply["diagnostics"].as_array().unwrap();
                let (category, locations) = match &error {
                    Error::Parse(errors) => (
                        "syntax",
                        errors.iter().map(|e| e.location()).collect::<Vec<_>>(),
                    ),
                    Error::Macro(error) => ("macro", vec![error.location()]),
                    _ => panic!("{id}: unexpected upstream error {error}"),
                };
                assert_eq!(case["category"], category, "{id}");
                assert_eq!(diagnostics.len(), locations.len(), "{id}");
                let mut actual_locations = Vec::new();
                for (entry, location) in diagnostics.iter().zip(locations) {
                    assert_eq!(entry["category"], category, "{id}");
                    assert!(!entry["message"].as_str().unwrap().is_empty(), "{id}");
                    assert_eq!(location.source(), source, "{id}");
                    assert_eq!(
                        entry["location"],
                        json!({
                            "offset": location.span().offset() + if source.starts_with('\u{feff}') { 3 } else { 0 },
                            "length": location.span().len()
                        }),
                        "{id}"
                    );
                    actual_locations.push(entry["location"].clone());
                }
                assert_eq!(json!(actual_locations), case["locations"], "{id}");
                eprintln!("{id}: {category} {}", json!(actual_locations));
            }
        }
    }
}
