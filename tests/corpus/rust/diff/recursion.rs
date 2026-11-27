// Recursive functions.
use farm::*;

fn fib(n: i32) -> i32 {
    if n < 2 {
        return n;
    }
    fib(n - 1) + fib(n - 2)
}

fn fact(n: i64) -> i64 {
    if n <= 1 { 1 } else { n * fact(n - 1) }
}

fn hanoi(n: i32, from: i32, to: i32, via: i32) -> i32 {
    if n == 0 {
        return 0;
    }
    hanoi(n - 1, from, via, to) + 1 + hanoi(n - 1, via, to, from)
}

fn sum_list(v: &Vec<i32>, i: usize) -> i32 {
    if i >= v.len() {
        0
    } else {
        v[i] + sum_list(v, i + 1)
    }
}

fn main() {
    let mut fibs = vec![];
    for i in 0..10 {
        fibs.push(fib(i));
    }
    println!("{:?}", fibs);
    println!("{}", fact(12));
    println!("{}", hanoi(5, 1, 3, 2));
    let v = vec![1, 2, 3, 4, 5];
    println!("{}", sum_list(&v, 0));
}
