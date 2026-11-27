#include "farm.h"

// Unlock what we can, then leave a note in the output.
int main(void) {
	if (num_unlocked(Unlocks.Expand) == 0) {
		unlock(Unlocks.Expand);
	}
	if (num_unlocked(Entities.Carrot) > 0 && num_items(Items.Hay) > 100) {
		unlock(Unlocks.Carrots);
	}
	set_execution_speed(10);
	set_world_size(4);
	quick_print("world %d, items %g, speed level %d", get_world_size(), num_items(Items.Wood), num_unlocked(Unlocks.Speed));
	pet_the_piggy();
	double r = random();
	if (r < 0.5) {
		do_a_flip();
	}
	return 0;
}
