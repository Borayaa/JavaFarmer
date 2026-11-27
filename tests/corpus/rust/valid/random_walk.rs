// Random walk with early returns, explicit number types and casts, r#move and bare directions.
use farm::*;

const DIRECTIONS: [Direction; 4] = [North, East, South, West];

fn pick() -> Direction {
    let r = random() * 4.0;
    let i = r as usize;
    DIRECTIONS[min(i, 3)]
}

fn try_harvest() -> bool {
    if !can_harvest() {
        return false;
    }
    harvest()
}

fn main() {
    let steps: u32 = 20;
    let mut harvested: i64 = 0;
    for _ in 0..steps {
        if try_harvest() {
            harvested += 1;
        }
        let dir = pick();
        if !r#move(dir) {
            move_to(Direction::North);
        }
    }
    let ratio = harvested as f64 / steps as f64;
    let distance = abs(get_pos_x() - get_pos_y());
    quick_print!("{} {} {}", harvested, ratio, max(distance, 1));
}
