#include "farm.h"

// Follow the right-hand wall to the treasure.
Direction dirs[4] = {North, East, South, West};

int turn_right(int d) {
	return (d + 1) % 4;
}

int turn_left(int d) {
	return (d + 3) % 4;
}

int main(void) {
	plant(Entities.Bush);
	while (get_entity_type() == Entities.Bush) {
		use_item(Items.Weird_Substance, get_world_size());
	}
	int dir = 0;
	while (get_entity_type() != Entities.Treasure) {
		int right = turn_right(dir);
		if (can_move(dirs[right])) {
			dir = right;
		} else if (!can_move(dirs[dir])) {
			dir = turn_left(dir);
			continue;
		}
		move(dirs[dir]);
	}
	harvest();
}
