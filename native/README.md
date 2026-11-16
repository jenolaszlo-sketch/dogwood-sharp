# Native foundation

The Rust crate is a buildable, empty cdylib scaffold, not a Dogwood wrapper.
No native binary is packaged at D0. The exact upstream dependency is exercised
and locked by `research/interop-spike/Cargo.toml` and `Cargo.lock`; D1 will move
that pin into the bridge after owner architecture acceptance. Keeping a heavy,
unused engine dependency out of an empty library is intentional.

Run `cargo build --locked --manifest-path native/dogwoodsharp-bridge/Cargo.toml`.
Do not enable `net` or `experimental-arithmetic` in the bridge.
