// A static mut is read and written only inside unsafe.
// ERROR: use of mutable static is unsafe and requires unsafe block (E0133)
// AT: COUNT
use farm::*;

static mut COUNT: i32 = 0;

fn main() {
    COUNT += 1;
}
