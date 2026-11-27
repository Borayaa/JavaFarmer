#include "farm.hpp"

// Every drone farms one column; lambdas capture the column number.
void farm_column(int column) {
	for (int i = 0; i < get_world_size(); i++) {
		if (can_harvest()) {
			harvest();
		}
		move(North);
	}
	quick_print("column {} done", column);
}

int main() {
	clear();
	for (int col = 0; col < get_world_size(); col++) {
		Drone d = spawn_drone([&]() { farm_column(col); });
		if (d == nullptr) {
			farm_column(col);
		}
		move(East);
	}
	while (num_drones() > 1) {
		do_a_flip();
	}
	auto last = spawn_drone([=] { do_a_flip(); });
	if (last && !has_finished(last)) {
		wait_for(last);
	}
}
