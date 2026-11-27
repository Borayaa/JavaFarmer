#include "farm.h"

// Every drone farms one column; when no drone is free the main drone does the work itself.
void farm_column(void) {
	for (int i = 0; i < get_world_size(); i++) {
		if (can_harvest()) {
			harvest();
		}
		move(North);
	}
}

int main(void) {
	clear();
	for (int col = 0; col < get_world_size(); col++) {
		Drone d = spawn_drone(farm_column);
		if (d == NULL) {
			farm_column();
		}
		move(East);
	}
	while (num_drones() > 1) {
		do_a_flip();
	}
	if (num_drones() < max_drones()) {
		Drone last = spawn_drone(&farm_column);
		if (last != NULL && !has_finished(last)) {
			wait_for(last);
		}
	}
	return 0;
}
