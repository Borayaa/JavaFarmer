// A static mut Vec cannot be borrowed in Rust 2024.
// ERROR: a `static mut` of type `Vec<i32>` is not supported in JavaFarmer (Rust 2024 forbids references to it): use a local variable and pass it as a parameter
// AT: SEEN: Vec<i32>
use farm::*;

static mut SEEN: Vec<i32> = Vec::new();

fn main() {
    harvest();
}
