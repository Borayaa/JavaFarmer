// while, loop with break, for over ranges (.., ..=, rev, step_by), continue.
use farm::*;

fn main() {
    let mut i = 0;
    let mut seen = String::new();
    while i < 5 {
        seen += &format!("{} ", i);
        i += 1;
    }
    println!("{seen}");
    let mut n = 27;
    let mut steps = 0;
    loop {
        if n == 1 {
            break;
        }
        if n % 2 == 0 {
            n /= 2;
        } else {
            n = 3 * n + 1;
        }
        steps += 1;
    }
    println!("collatz steps: {steps}");
    let mut total = 0;
    for k in 1..=10 {
        if k % 3 == 0 {
            continue;
        }
        total += k;
    }
    println!("total {}", total);
    let mut down = Vec::new();
    for k in (0..10).rev().step_by(3) {
        down.push(k);
    }
    println!("{:?}", down);
    let mut up = Vec::new();
    for k in (5..20).step_by(4) {
        up.push(k);
    }
    println!("{:?}", up);
    for row in 0..3 {
        let mut line = String::new();
        for col in 0..=row {
            line.push_str(&format!("{}", row * col));
        }
        println!("{line}");
    }
}
