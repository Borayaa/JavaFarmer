// Wear the dinosaur hat and steer toward apples using measure().
use farm::*;

fn step(d: Direction, path: &mut Vec<Direction>) -> bool {
    if move_to(d) {
        path.push(d);
        return true;
    }
    false
}

fn main() {
    let mut path: Vec<Direction> = Vec::new();
    change_hat(Hat::Dinosaur_Hat);
    loop {
        let target: Option<Position> = measure();
        if target == None {
            break;
        }
        let (apple_x, apple_y) = target.unwrap();
        let moved;
        if get_pos_x() < apple_x {
            moved = step(East, &mut path);
        } else if get_pos_x() > apple_x {
            moved = step(West, &mut path);
        } else if get_pos_y() < apple_y {
            moved = step(North, &mut path);
        } else {
            moved = step(South, &mut path);
        }
        if !moved {
            break;
        }
    }
    quick_print!("steps {}", path.len());
    change_hat(Hat::Straw_Hat);
}
