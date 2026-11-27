#include "farm.h"
#include <stdio.h>

// Check what we can afford before unlocking.
bool can_afford(Unlock u) {
	Map(Item, double) cost = get_cost(u);
	if (cost == NULL) {
		return false;
	}
	for_each (Item item, cost) {
		if (num_items(item) < map_get(cost, item)) {
			return false;
		}
	}
	return true;
}

int main(void) {
	Unlock wanted[] = {Unlocks.Speed, Unlocks.Expand, Unlocks.Plant};
	for (int i = 0; i < 3; i++) {
		if (can_afford(wanted[i]) && num_unlocked(wanted[i]) < 5) {
			unlock(wanted[i]);
		}
	}
	Map(Item, double) level2 = get_cost(Unlocks.Speed, 2);
	if (level2 != NULL) {
		printf("speed 2 needs %d items\n", map_len(level2));
	}
	change_hat(Hats.Straw_Hat);
	set_execution_speed(2);
	double t = get_time();
	quick_print("ticks %g time %g", get_tick_count(), t);
	return 0;
}
