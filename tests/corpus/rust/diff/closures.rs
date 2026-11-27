// Closures and fn items as values.
use farm::*;

fn double(x: i32) -> i32 {
    x * 2
}

fn apply(f: fn(i32) -> i32, x: i32) -> i32 {
    f(x)
}

fn apply_twice(f: fn(i32) -> i32, x: i32) -> i32 {
    f(f(x))
}

fn main() {
    let add = |a: i32, b: i32| a + b;
    println!("{}", add(2, 3));
    let square = |x: i32| x * x;
    println!("{}", apply(square, 7));
    println!("{}", apply(double, 7));
    println!("{}", apply_twice(double, 3));
    let offset = 10;
    let shift = move |x: i32| x + offset;
    println!("{}", shift(5));
    let f: fn(i32) -> i32 = double;
    println!("{}", f(21));
    let steps = || {
        let mut s = 0;
        for i in 0..4 {
            s += i;
        }
        s
    };
    println!("{}", steps());
}
