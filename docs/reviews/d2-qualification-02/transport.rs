//! Process-isolated test image. It serves local fixtures and audits buffer ownership.
use std::{
    collections::BTreeMap,
    env, fs,
    io::Write,
    path::PathBuf,
    ptr, slice,
    sync::{Mutex, OnceLock},
};

#[repr(C)]
pub struct Buffer {
    data: *mut u8,
    length: u32,
}

const EMPTY: Buffer = Buffer {
    data: ptr::null_mut(),
    length: 0,
};

#[derive(Default)]
struct Ownership {
    next: u32,
    live: BTreeMap<usize, (u32, u32, u32)>,
}

fn ownership() -> &'static Mutex<Ownership> {
    static STATE: OnceLock<Mutex<Ownership>> = OnceLock::new();
    STATE.get_or_init(Mutex::default)
}

fn log(row: &str) {
    let path = env::var_os("Q2_EVENTS").unwrap();
    let mut file = fs::OpenOptions::new()
        .create(true)
        .append(true)
        .open(path)
        .unwrap();
    writeln!(file, "{row}").unwrap();
}

#[unsafe(no_mangle)]
pub extern "C" fn dogwoodsharp_abi_version() -> u32 {
    let _lock = ownership().lock().unwrap();
    log("abi");
    1
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn dogwoodsharp_call_v1(
    abi: u32,
    op: u32,
    input: *const u8,
    length: u32,
    output: *mut Buffer,
) -> u32 {
    assert!(abi == 1 && !output.is_null());
    unsafe { output.write(EMPTY) };
    let (path, status) = match op {
        0 => (env::var_os("Q2_IDENTITY").unwrap().into(), 0),
        3 => {
            let request =
                str::from_utf8(unsafe { slice::from_raw_parts(input, length as usize) }).unwrap();
            let id = request
                .split_once("\"source\":\"")
                .unwrap()
                .1
                .split('"')
                .next()
                .unwrap();
            assert!(!id.is_empty() && id.bytes().all(|c| c.is_ascii_alphanumeric() || c == b'-'));
            let mut path = PathBuf::from(env::var_os("Q2_PAYLOADS").unwrap());
            path.push(format!("{id}.json"));
            (path, if id == "status-with-buffer" { 8 } else { 0 })
        }
        _ => return 3,
    };
    let reply = fs::read(path).unwrap().into_boxed_slice();
    assert!(!reply.is_empty() && reply.len() <= 8 * 1024 * 1024 + 1);
    let length = reply.len() as u32;
    let data = Box::into_raw(reply).cast::<u8>();
    let mut owner = ownership().lock().unwrap();
    owner.next += 1;
    let id = owner.next;
    assert!(owner.live.insert(data as usize, (op, id, length)).is_none());
    log(&format!("alloc {op} {id}"));
    unsafe { output.write(Buffer { data, length }) };
    status
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn dogwoodsharp_free_v1(output: *mut Buffer) {
    let buffer = unsafe { output.as_mut() }.unwrap();
    let mut owner = ownership().lock().unwrap();
    let (op, id, length) = owner.live.remove(&(buffer.data as usize)).unwrap();
    assert_eq!(length, buffer.length);
    let bytes = ptr::slice_from_raw_parts_mut(buffer.data, length as usize);
    unsafe { drop(Box::from_raw(bytes)) };
    *buffer = EMPTY;
    log(&format!("free {op} {id}"));
}
