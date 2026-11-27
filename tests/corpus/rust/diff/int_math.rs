// Integer division and remainder truncate toward zero; div_euclid / rem_euclid floor.
use farm::*;

fn gcd(a: i32, b: i32) -> i32 {
    if b == 0 { a } else { gcd(b, a % b) }
}

fn main() {
    let values = [7, -7, 13, -13, 0, 100];
    for &a in &values {
        for b in [3, -3, 5] {
            println!("{} / {} = {}, {} % {} = {}", a, b, a / b, a, b, a % b);
        }
    }
    println!("{}", (-7_i32).div_euclid(2));
    println!("{}", (-7_i32).rem_euclid(2));
    let mut x = 100;
    x /= -7;
    println!("{x}");
    x %= 4;
    println!("{x}");
    println!("{}", gcd(84, 36));
    println!("{}", 2_i32.pow(10));
    println!("{} {} {}", abs(-5), min(3, -4), max(3, -4));
    let big: i64 = 1_000_000;
    println!("{}", big * 1000);
    let f = 7 as f64 / 2.0;
    println!("{}", f);
    println!("{}", (f * 2.0) as i32);
    println!("{}", (-2.5_f64) as i32);
    println!("{}", -7 / 2 * 2 + -7 % 2);
    let n: u32 = 17;
    println!("{}", n / 4 + n % 4);
}
