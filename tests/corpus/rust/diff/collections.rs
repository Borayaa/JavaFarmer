// Vec, HashMap and HashSet operations (printed in a fixed order).
use farm::*;
use std::collections::{HashMap, HashSet};

fn main() {
    let mut v = vec![5, 3, 8, 1];
    v.push(7);
    v.insert(0, 9);
    println!("{:?} {}", v, v.len());
    let last = v.pop().unwrap();
    println!("{last} {:?}", v);
    v.remove(1);
    println!("{:?}", v);
    println!("{:?} {}", v, v.contains(&8));
    v[0] = 42;
    println!("{}", v[0] + v[v.len() - 1]);
    let grid = vec![vec![0; 3]; 2];
    println!("{:?}", grid);
    let names = vec!["a", "b"];
    println!("{:?}", names);

    let mut counts: HashMap<i32, i32> = HashMap::new();
    for x in [1, 2, 2, 3, 3, 3] {
        *counts.entry(x).or_insert(0) += 1;
    }
    for k in 1..=4 {
        println!("{} -> {}", k, *counts.get(&k).unwrap_or(&0));
    }
    println!("{} {}", counts.contains_key(&2), counts.len());
    counts.remove(&2);
    println!("{}", counts.contains_key(&2));
    counts.insert(9, 99);
    println!("{}", counts[&9]);

    let mut seen = HashSet::new();
    for x in [4, 1, 4, 2, 1] {
        seen.insert(x);
    }
    println!("{} {} {}", seen.len(), seen.contains(&4), seen.contains(&3));
    seen.remove(&4);
    println!("{}", seen.len());
    let copy = v.clone();
    v.clear();
    println!("{} {}", v.len(), copy.len());
    let pairs = vec![(1, 2), (3, 4)];
    for (a, b) in &pairs {
        println!("{}", a * b);
    }
    for (i, x) in copy.iter().enumerate() {
        println!("{i}:{x}");
    }
}
