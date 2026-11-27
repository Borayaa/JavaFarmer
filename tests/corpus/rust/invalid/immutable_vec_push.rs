// Pushing onto a Vec that is not `mut`.
// ERROR: cannot borrow `crops` as mutable, as it is not declared as mutable (make it `let mut crops`) (E0596)
// AT: crops
use farm::*;

fn main() {
    let crops = Vec::new();
    crops.push(Entity::Carrot);
}
