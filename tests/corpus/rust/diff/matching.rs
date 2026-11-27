// match as a statement and as a value, or-patterns, string matches, if-else chains.
use farm::*;

fn describe(n: i32) -> &'static str {
    match n {
        0 => "zero",
        1 | 2 | 3 => "small",
        4 => "four",
        _ => "big",
    }
}

fn bonus(name: &str) -> i32 {
    match name {
        "hay" => 1,
        "wood" => 2,
        _ => 0,
    }
}

fn main() {
    for n in 0..6 {
        println!("{} {}", n, describe(n));
    }
    let words = vec!["hay", "wood", "carrot"];
    let mut sum = 0;
    for w in &words {
        sum += bonus(w);
    }
    println!("{sum}");
    let grade = 73;
    let letter = if grade >= 90 {
        "A"
    } else if grade >= 70 {
        "C"
    } else {
        "F"
    };
    println!("{letter}");
    let x = 5;
    let kind = match x % 3 {
        0 => {
            println!("divisible");
            "zero"
        }
        1 => "one",
        _ => "two",
    };
    println!("{}", kind);
    let big = x > 3;
    match big {
        true => println!("big"),
        false => println!("small"),
    }
}
