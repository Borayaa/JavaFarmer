#include "farm.hpp"
#include <vector>

// Follow the right-hand wall to the treasure.
const std::vector<Direction> dirs = {North, East, South, West};

int turn_right(int d) {
	return (d + 1) % 4;
}

int main() {
	plant(Entities::Bush);
	while (get_entity_type() == Entities::Bush) {
		use_item(Items::Weird_Substance, get_world_size());
	}
	int dir = 0;
	while (get_entity_type() != Entities::Treasure) {
		int right = turn_right(dir);
		if (can_move(dirs[right])) {
			dir = right;
		} else if (!can_move(dirs[dir])) {
			dir = (dir + 3) % 4;
			continue;
		}
		move(dirs[dir]);
	}
	Position next = measure();
	quick_print("next treasure at {} {}", next.x, next.y);
	harvest();
}
