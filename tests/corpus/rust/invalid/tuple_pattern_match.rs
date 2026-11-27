// Tuple patterns in match.
// ERROR: this `match` pattern is not supported in JavaFarmer
// AT: (0, 0)
use farm::*;

fn main() {
    let pos = (get_pos_x(), get_pos_y());
    match pos {
        (0, 0) => {
            harvest();
        }
        _ => {}
    }
}
