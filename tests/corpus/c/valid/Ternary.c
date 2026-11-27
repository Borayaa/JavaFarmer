#include "farm.h"

// ?: as a whole value: a declaration, an assignment, an argument, a return value.
Entity crop_for(int x) {
	return x % 3 == 0 ? Entities.Carrot : Entities.Grass;
}

int main(void) {
	int n = get_world_size();
	for (int x = 0; x < n; x++) {
		int steps = x % 2 == 0 ? n : n - 1;
		Direction d = x < n / 2 ? North : South;
		for (int i = 0; i < steps; i++) {
			if (get_ground_type() == Grounds.Grassland) till();
			plant(crop_for(x));
			move(d);
		}
		move(can_move(East) ? East : West);
	}
	return 0;
}
