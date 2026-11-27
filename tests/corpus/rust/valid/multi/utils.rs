// A library window: pub items are visible to the other windows.
use farm::*;

pub const LIMIT: i32 = 42;
const SECRET: i32 = 7;

pub fn size() -> i32 {
    get_world_size()
}

/// Moves to (x, y) and returns the number of moves.
pub fn go_to(x: i32, y: i32) -> i32 {
    let mut moves = 0;
    while get_pos_x() != x {
        move_to(West);
        moves += 1;
    }
    while get_pos_y() != y {
        move_to(South);
        moves += 1;
    }
    moves + SECRET - SECRET
}
