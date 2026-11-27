// Try on every hat in a list.
use farm::*;

fn main() {
    let hats = vec![Hat::Gray_Hat, Hat::Purple_Hat, Hat::Green_Hat, Hat::Brown_Hat];
    for hat in hats {
        change_hat(hat);
        do_a_flip();
    }
    pet_the_piggy();
}
