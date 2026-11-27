#include "farm.h"

// Replant dead pumpkins until the whole field is one big pumpkin.
List(int) dead_x;
List(int) dead_y;

void go_to(int x, int y) {
	while (get_pos_x() != x) {
		move(East);
	}
	while (get_pos_y() != y) {
		move(North);
	}
}

void plant_all(void) {
	int n = get_world_size();
	for (int x = 0; x < n; x++) {
		for (int y = 0; y < n; y++) {
			if (get_ground_type() != Grounds.Soil) {
				till();
			}
			plant(Entities.Pumpkin);
			list_add(dead_x, get_pos_x());
			list_add(dead_y, get_pos_y());
			move(North);
		}
		move(East);
	}
}

int main(void) {
	dead_x = list_new(int);
	dead_y = list_new(int);
	clear();
	plant_all();
	while (!list_is_empty(dead_x)) {
		int x = list_remove_at(dead_x, 0);
		int y = list_remove_at(dead_y, 0);
		go_to(x, y);
		if (get_entity_type() == Entities.Pumpkin && can_harvest()) {
			continue;
		}
		plant(Entities.Pumpkin);
		list_add(dead_x, x);
		list_add(dead_y, y);
	}
	harvest();
	return 0;
}
