// impl blocks.
// ERROR: an `impl` block is not supported in JavaFarmer
// AT: impl
use farm::*;

fn main() {
    harvest();
}

impl Entity {
    fn grow(&self) {}
}
