#include "farm.h"

// Functions as values: pass them to helpers and to spawn_drone.
void harvest_here(void) {
	if (can_harvest()) {
		harvest();
	}
}

void plant_grass(void) {
	plant(Entities.Grass);
}

void for_each_tile(void (*action)(void)) {
	int n = get_world_size();
	for (int x = 0; x < n; x++) {
		for (int y = 0; y < n; y++) {
			action();
			move(North);
		}
		move(East);
	}
}

void sweep(void) {
	for_each_tile(harvest_here);
}

int main(void) {
	void (*job)(void) = plant_grass;
	for_each_tile(job);
	job = &harvest_here;
	job();
	if (spawn_drone(sweep) == NULL) {
		sweep();
	}
	return 0;
}
