#include "farm.h"

// Trees on the black squares, bushes on the white ones.
void plant_tile(int x, int y) {
	if ((x + y) % 2 == 0) {
		plant(Entities.Tree);
	} else {
		plant(Entities.Bush);
	}
}

int main(void) {
	int n = get_world_size();
	while (true) {
		for (int x = 0; x < n; x++) {
			for (int y = 0; y < n; y++) {
				if (can_harvest()) {
					harvest();
				}
				if (get_entity_type() == NULL) {
					plant_tile(get_pos_x(), get_pos_y());
				}
				move(North);
			}
			move(East);
		}
	}
}
