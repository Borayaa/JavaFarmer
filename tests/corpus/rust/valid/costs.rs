// Check what we can afford before unlocking.
use farm::*;

fn can_afford(u: Unlock) -> bool {
    let cost = get_cost(u);
    if cost.is_none() {
        return false;
    }
    let cost = cost.unwrap();
    for (item, amount) in &cost {
        if num_items(*item) < *amount {
            return false;
        }
    }
    true
}

fn main() {
    let wanted = [Unlock::Speed, Unlock::Expand, Unlock::Plant];
    for &u in &wanted {
        if can_afford(u) && num_unlocked(u) < 5 {
            unlock(u);
        }
    }
    if let Some((plant_type, (x, y))) = get_companion() {
        quick_print!("{} {} {}", plant_type, x, y);
    }
    change_hat(Hat::Straw_Hat);
    set_execution_speed(2.0);
    let t = get_time();
    quick_print!("ticks {} time {}", get_tick_count(), t);
    let level_cost = get_cost_with(Unlock::Loops, 0);
    println!("{:?}", level_cost.is_some());
}
