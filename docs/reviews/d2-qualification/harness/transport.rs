//! Bounded qualification substitute for managed response transport. It runs
//! in an isolated consumer directory and reads only the fixture paths set by
//! that process's launcher.
use std::{env, fs, ptr};

#[repr(C)]
pub struct Buffer {
    data: *mut u8,
    length: u32,
}

const EMPTY: Buffer = Buffer {
    data: ptr::null_mut(),
    length: 0,
};

#[unsafe(no_mangle)]
pub extern "C" fn dogwoodsharp_abi_version() -> u32 {
    1
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn dogwoodsharp_call_v1(
    abi: u32,
    op: u32,
    _: *const u8,
    _: u32,
    output: *mut Buffer,
) -> u32 {
    if output.is_null() {
        return 1;
    }
    unsafe { output.write(EMPTY) };
    let key = match (abi, op) {
        (1, 0) => "D2Q_IDENTITY",
        (1, 3) => "D2Q_REPLY",
        _ => return 3,
    };
    let Some(path) = env::var_os(key) else {
        return 8;
    };
    let Ok(bytes) = fs::read(path) else {
        return 8;
    };
    if bytes.len() > 8 * 1024 * 1024 + 1 {
        return 6;
    }
    let boxed = bytes.into_boxed_slice();
    let length = boxed.len() as u32;
    let data = Box::into_raw(boxed).cast::<u8>();
    unsafe { output.write(Buffer { data, length }) };
    0
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn dogwoodsharp_free_v1(output: *mut Buffer) {
    if let Some(buffer) = unsafe { output.as_mut() } {
        if !buffer.data.is_null() {
            let bytes = ptr::slice_from_raw_parts_mut(buffer.data, buffer.length as usize);
            unsafe { drop(Box::from_raw(bytes)) };
        }
        *buffer = EMPTY;
    }
}
