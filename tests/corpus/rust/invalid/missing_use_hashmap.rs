// HashMap needs `use std::collections::HashMap;`.
// ERROR: failed to resolve: use of undeclared type `HashMap` (add `use std::collections::HashMap;`) (E0433)
// AT: HashMap
use farm::*;

fn main() {
    let mut counts = HashMap::new();
    counts.insert(1, 2);
}
