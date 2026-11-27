#include "farm.h"

/* Carrots between trees; water when the ground gets dry. */
bool is_tree_tile(void) {
	return (get_pos_x() + get_pos_y()) % 2 == 0;
}

void tend(void) {
	if (get_water() < 0.5 && num_items(Items.Water) > 0) {
		use_item(Items.Water);
	}
	if (can_harvest()) {
		harvest();
	}
	if (is_tree_tile()) {
		plant(Entities.Tree);
	} else {
		if (get_ground_type() == Grounds.Grassland) {
			till();
		}
		plant(Entities.Carrot);
	}
}

int main(void) {
	const int size = get_world_size();
	for (;;) {
		for (int i = 0; i < size * size; i++) {
			tend();
			move(North);
			if (get_pos_y() == 0) {
				move(East);
			}
		}
	}
}
