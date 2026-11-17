//! Test-only native asset. Loading this image must fail before operation lookup.
#[unsafe(no_mangle)]
pub extern "C" fn dogwoodsharp_abi_version() -> u32 {
    2
}
