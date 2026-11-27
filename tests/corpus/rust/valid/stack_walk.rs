// A Vec used as a stack of tiles to visit: while let / if let on pop(), &mut parameters, iteration forms.
use farm::*;
use std::collections::HashMap;

fn plan(path: &mut Vec<Direction>, n: i32) {
    for i in 0..n {
        if i % 2 == 0 {
            path.push(North);
        } else {
            path.push(Direction::East);
        }
    }
}

fn count_ready(tiles: &Vec<bool>) -> i32 {
    let mut ready = 0;
    for t in tiles.iter() {
        if *t {
            ready += 1;
        }
    }
    ready
}

fn main() {
    let mut path = Vec::new();
    plan(&mut path, get_world_size());
    let mut tiles: Vec<bool> = vec![];
    while let Some(dir) = path.pop() {
        tiles.push(can_harvest());
        if can_harvest() {
            harvest();
        }
        move_to(dir);
    }
    for (i, ready) in tiles.iter().enumerate() {
        if *ready {
            quick_print!("tile {} was ready", i);
        }
    }
    let mut stock: HashMap<Item, f64> = HashMap::new();
    stock.insert(Item::Hay, num_items(Item::Hay));
    stock.insert(Item::Wood, num_items(Item::Wood));
    for (item, amount) in &stock {
        quick_print!("{:?} {}", item, amount);
    }
    let backup = tiles.clone();
    if let Some(last) = tiles.pop() {
        quick_print!("last {} of {}", last, backup.len());
    }
    quick_print!("{}", count_ready(&backup));
}
