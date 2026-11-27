// Strings: format!, push_str, concatenation, comparisons and common methods.
use farm::*;

fn greet(name: &str) -> String {
    format!("hello, {name}!")
}

fn main() {
    let mut s = String::from("farm");
    s.push_str("er");
    println!("{} {}", s, s.len());
    let t = s.clone() + " bot";
    println!("{t}");
    println!("{}", greet("drone"));
    println!("{} {}", t.contains("bot"), t.contains("x"));
    let n = 42;
    let msg = format!("{} + {} = {}", n, 1, n + 1);
    println!("{msg}");
    let joined = format!("{}{}", "ab", "cd");
    println!("{}", joined == "abcd");
    let pi = 3.25;
    println!("{} {:?}", pi, pi * 2.0);
    println!("{}", n.to_string() + "!");
    let mut line = String::new();
    for i in 0..3 {
        line += &i.to_string();
    }
    println!("{line}");
    println!("{:?}", vec!["x", "y"]);
}
