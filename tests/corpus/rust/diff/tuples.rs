// Tuples, tuple returns, destructuring, shadowing and constants.
use farm::*;

const LIMIT: i32 = 4;
static GREETING: &str = "hi";

fn min_max(v: &Vec<i32>) -> (i32, i32) {
    let mut lo = v[0];
    let mut hi = v[0];
    for &x in v {
        if x < lo {
            lo = x;
        }
        if x > hi {
            hi = x;
        }
    }
    (lo, hi)
}

fn main() {
    let (lo, hi) = min_max(&vec![3, -2, 9, 4]);
    println!("{lo} {hi}");
    let p = (1, 2);
    println!("{:?} {} {}", p, p.0, p.1);
    let (mut a, mut b) = (1, 1);
    for _ in 0..LIMIT {
        let t = a + b;
        a = b;
        b = t;
    }
    println!("{a} {b}");
    let x = 5;
    let x = x * 2;
    let x = x + 1;
    println!("{x} {}", GREETING);
}
