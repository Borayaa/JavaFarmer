// Module-level state: const for fixed values, static mut (inside unsafe) for counters.
use farm::*;

const TARGET: i32 = 10;
static mut HARVESTED: i32 = 0;

fn harvest_if_ready() {
    if can_harvest() {
        harvest();
        unsafe {
            HARVESTED += 1;
        }
    }
}

fn main() {
    let size = get_world_size();
    for _ in 0..size {
        harvest_if_ready();
        move_to(North);
    }
    let done = unsafe { HARVESTED };
    let avg = done as f64 / size as f64;
    quick_print!("harvested {} of {} avg {}", done, TARGET, avg);
    quick_print!("rounded {} floor {}", avg.round(), avg.floor());
    spawn_drone(harvest_if_ready);
}
