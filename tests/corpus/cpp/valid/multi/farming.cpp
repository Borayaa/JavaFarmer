#include "farm.hpp"

void walk(int steps);
int rows_done = 0;

void plant_row(Entity e) {
	for (int i = 0; i < get_world_size(); i++) {
		plant(e);
		walk(1);
	}
	rows_done++;
}

int harvest_row() {
	int count = 0;
	for (int i = 0; i < get_world_size(); i++) {
		if (can_harvest() && harvest()) {
			count++;
		}
		walk(1);
	}
	return count;
}
