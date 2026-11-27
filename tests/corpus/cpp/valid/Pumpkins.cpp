#include "farm.hpp"
#include <vector>

// Replant dead pumpkins until the whole field is one big pumpkin.
std::vector<Position> todo;

void go_to(Position p) {
	while (get_pos_x() != p.x) {
		move(East);
	}
	while (get_pos_y() != p.y) {
		move(North);
	}
}

void plant_all() {
	int n = get_world_size();
	for (int x = 0; x < n; x++) {
		for (int y = 0; y < n; y++) {
			if (get_ground_type() != Grounds::Soil) {
				till();
			}
			plant(Entities::Pumpkin);
			todo.push_back(Position{get_pos_x(), get_pos_y()});
			move(North);
		}
		move(East);
	}
}

int main() {
	clear();
	plant_all();
	while (!todo.empty()) {
		Position p = todo.front();
		todo.erase(todo.begin());
		go_to(p);
		if (get_entity_type() == Entities::Pumpkin && can_harvest()) {
			continue;
		}
		plant(Entities::Pumpkin);
		todo.push_back(p);
	}
	harvest();
	return 0;
}
