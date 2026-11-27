// The first program: harvest hay forever.
use farm::*;

fn main() {
    loop {
        if can_harvest() {
            harvest();
        }
    }
}
