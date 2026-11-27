// Sort a row of cacti with bubble sort using measure() and swap().
use farm::*;

fn sort_row() {
    let n = get_world_size();
    let mut swapped = true;
    while swapped {
        swapped = false;
        for _ in 0..n - 1 {
            let here: i32 = measure();
            let next: i32 = measure_with(East);
            if here > next {
                swap(East);
                swapped = true;
            }
            move_to(East);
        }
        for _ in 0..n - 1 {
            move_to(West);
        }
    }
}

fn main() {
    let size = get_world_size();
    let mut heights = vec![0; size as usize];
    for x in 0..size {
        if get_ground_type() != Ground::Soil {
            till();
        }
        plant(Entity::Cactus);
        heights[x as usize] = measure();
        move_to(East);
    }
    let mut tallest = 0;
    for &h in &heights {
        tallest = max(tallest, h);
    }
    quick_print!("tallest {}", tallest);
    sort_row();
    harvest();
}
