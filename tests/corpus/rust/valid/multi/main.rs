// Uses helpers from two other code windows (modules).
use farm::*;

mod farming;
mod utils;

fn main() {
    let mut moves = utils::go_to(0, 0);
    let n = utils::size();
    let mut planted = 0;
    for _ in 0..n {
        if farming::tend_tile() {
            planted += 1;
        }
        moves += 1;
        move_to(North);
    }
    quick_print!("moves: {} planted: {} limit: {}", moves, planted, utils::LIMIT);
    spawn_drone(farming::tend_tile);
}
