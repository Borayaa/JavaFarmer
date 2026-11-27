#include "farm.hpp"

// Unlock what we can, then leave a note in the output.
int main() {
	if (num_unlocked(Unlocks::Expand) == 0) {
		unlock(Unlocks::Expand);
	}
	if (num_unlocked(Entities::Carrot) > 0 && num_items(Items::Hay) > 100) {
		unlock(Unlocks::Carrots);
	}
	set_execution_speed(10);
	quick_print("world {}, items {}, speed level {}", get_world_size(), num_items(Items::Wood), num_unlocked(Unlocks::Speed));
	pet_the_piggy();
	if (farm::random() < 0.5) {
		do_a_flip();
	}
}
