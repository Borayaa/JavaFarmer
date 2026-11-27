// Measure petals and harvest the biggest sunflower first (Option<f64> from measure).
use farm::*;

fn main() {
    let n = get_world_size();
    let mut best = 0.0;
    let mut best_x = 0;
    for x in 0..n {
        if get_ground_type() != Ground::Soil {
            till();
        }
        plant(Entity::Sunflower);
        if let Some(petals) = measure() {
            if petals > best {
                best = petals;
                best_x = x;
            }
        }
        move_to(East);
    }
    quick_print!("best {} at {}", best, best_x);
}
