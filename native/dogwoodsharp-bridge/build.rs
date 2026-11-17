fn main() {
    println!("cargo:rerun-if-changed=build.rs");
    println!(
        "cargo:rustc-env=DOGWOODSHARP_TARGET={}",
        std::env::var("TARGET").expect("Cargo target")
    );
}
