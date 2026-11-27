// let shadowing, block scopes, immutable bindings initialised later.
use farm::*;

fn main() {
    let x = get_world_size();
    let x = x * 2;
    let label;
    if x > 10 {
        label = "big";
    } else {
        label = "small";
    }
    {
        let x = 1;
        quick_print!("inner {}", x);
    }
    let r#type = 3;
    quick_print!("{} {} {}", x, label, r#type);
    r#move(North);
}
