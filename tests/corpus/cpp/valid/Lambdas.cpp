#include "farm.hpp"
#include <functional>

// Lambdas as values: store them, pass them, give them to drones.
// A lambda can read the locals around it, but it changes a global: the game has no nonlocal.
void for_each_tile(const std::function<void()>& action) {
	int n = get_world_size();
	for (int x = 0; x < n; x++) {
		for (int y = 0; y < n; y++) {
			action();
			move(North);
		}
		move(East);
	}
}

int planted = 0;

void harvest_here() {
	if (can_harvest()) {
		harvest();
	}
}

int main() {
	auto plant_grass = [&]() {
		plant(Entities::Grass);
		planted++;
	};
	for_each_tile(plant_grass);
	std::function<void()> job = harvest_here;
	job();
	auto sweep = [&] { for_each_tile(harvest_here); };
	if (!spawn_drone(sweep)) {
		sweep();
	}
	quick_print("planted {}", planted);
}
