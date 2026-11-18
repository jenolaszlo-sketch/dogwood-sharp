//! Minimal loader for the exported DogwoodSharp C ABI, so the differential
//! exercises the same symbols a native consumer uses.

use std::ffi::c_void;

#[repr(C)]
#[derive(Clone, Copy)]
pub struct Buffer {
    pub data: *mut u8,
    pub len: u32,
}

impl Buffer {
    const EMPTY: Self = Self {
        data: std::ptr::null_mut(),
        len: 0,
    };
}

type AbiVersionFn = unsafe extern "C" fn() -> u32;
type CallFn = unsafe extern "C" fn(u32, u32, *const u8, u32, *mut Buffer) -> u32;
type FreeFn = unsafe extern "C" fn(*mut Buffer);

#[cfg(windows)]
mod sys {
    use std::ffi::c_void;
    use std::os::raw::c_char;

    #[link(name = "kernel32")]
    unsafe extern "system" {
        fn LoadLibraryA(name: *const c_char) -> *mut c_void;
        fn GetProcAddress(handle: *mut c_void, name: *const c_char) -> *mut c_void;
        fn FreeLibrary(handle: *mut c_void) -> i32;
        fn GetLastError() -> u32;
    }

    pub fn open(path: &str) -> *mut c_void {
        let name = std::ffi::CString::new(path).unwrap_or_default();
        unsafe { LoadLibraryA(name.as_ptr()) }
    }

    pub fn close(handle: *mut c_void) {
        unsafe { FreeLibrary(handle) };
    }

    pub fn symbol(handle: *mut c_void, name: &str) -> *mut c_void {
        let name = std::ffi::CString::new(name).unwrap_or_default();
        unsafe { GetProcAddress(handle, name.as_ptr()) }
    }

    pub fn last_error() -> String {
        format!("windows error {}", unsafe { GetLastError() })
    }
}

#[cfg(unix)]
mod sys {
    use std::ffi::c_void;
    use std::os::raw::c_char;

    #[cfg(target_os = "linux")]
    #[link(name = "dl")]
    unsafe extern "C" {}

    unsafe extern "C" {
        fn dlopen(filename: *const c_char, flag: i32) -> *mut c_void;
        fn dlsym(handle: *mut c_void, symbol: *const c_char) -> *mut c_void;
        fn dlclose(handle: *mut c_void) -> i32;
        fn dlerror() -> *mut c_char;
    }

    const RTLD_NOW: i32 = 2;

    pub fn open(path: &str) -> *mut c_void {
        let name = std::ffi::CString::new(path).unwrap_or_default();
        unsafe { dlopen(name.as_ptr(), RTLD_NOW) }
    }

    pub fn close(handle: *mut c_void) {
        unsafe { dlclose(handle) };
    }

    pub fn symbol(handle: *mut c_void, name: &str) -> *mut c_void {
        let name = std::ffi::CString::new(name).unwrap_or_default();
        unsafe { dlsym(handle, name.as_ptr()) }
    }

    pub fn last_error() -> String {
        let message = unsafe { dlerror() };
        if message.is_null() {
            return "unknown loader error".to_string();
        }
        unsafe { std::ffi::CStr::from_ptr(message) }
            .to_string_lossy()
            .into_owned()
    }
}

pub struct Library {
    handle: *mut c_void,
    call: CallFn,
    free: FreeFn,
}

// The library outlives every buffer, and each call returns an owned buffer that
// the harness copies before releasing it.
unsafe impl Send for Library {}

impl Library {
    pub fn open(path: &str) -> Result<Self, String> {
        let handle = sys::open(path);
        if handle.is_null() {
            return Err(format!("cannot load {path}: {}", sys::last_error()));
        }

        let abi = sys::symbol(handle, "dogwoodsharp_abi_version") as usize;
        let call = sys::symbol(handle, "dogwoodsharp_call_v1") as usize;
        let free = sys::symbol(handle, "dogwoodsharp_free_v1") as usize;
        if abi == 0 || call == 0 || free == 0 {
            sys::close(handle);
            return Err(format!("{path} is missing a v1 export"));
        }

        let abi: AbiVersionFn = unsafe { std::mem::transmute(abi) };
        let version = unsafe { abi() };
        if version != 1 {
            sys::close(handle);
            return Err(format!("{path} exports ABI {version}, expected 1"));
        }

        Ok(Self {
            handle,
            call: unsafe { std::mem::transmute::<usize, CallFn>(call) },
            free: unsafe { std::mem::transmute::<usize, FreeFn>(free) },
        })
    }

    /// Invoke one operation. Returns the owned reply bytes, or the raw status.
    pub fn call(&self, operation: u32, request: &[u8]) -> Result<Vec<u8>, u32> {
        let mut output = Buffer::EMPTY;
        let status = unsafe {
            (self.call)(
                1,
                operation,
                request.as_ptr(),
                request.len() as u32,
                &mut output,
            )
        };
        if status != 0 {
            unsafe { (self.free)(&mut output) };
            return Err(status);
        }
        if output.data.is_null() || output.len == 0 {
            unsafe { (self.free)(&mut output) };
            return Err(u32::MAX);
        }
        let bytes =
            unsafe { std::slice::from_raw_parts(output.data, output.len as usize) }.to_vec();
        unsafe { (self.free)(&mut output) };
        Ok(bytes)
    }
}

impl Drop for Library {
    fn drop(&mut self) {
        sys::close(self.handle);
    }
}
