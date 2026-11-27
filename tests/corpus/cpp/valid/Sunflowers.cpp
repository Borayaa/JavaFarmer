#include "farm.hpp"
#include <iostream>
#include <map>

// Count the sunflowers by petals.
int main() {
	std::map<int, int> by_petals;
	int n = get_world_size();
	for (int x = 0; x < n; x++) {
		for (int y = 0; y < n; y++) {
			if (get_ground_type() != Grounds::Soil) till();
			plant(Entities::Sunflower);
			int petals = measure();
			if (by_petals.count(petals)) {
				by_petals[petals] += 1;
			} else {
				by_petals[petals] = 1;
			}
			move(North);
		}
		move(East);
	}
	for (auto& [petals, count] : by_petals) {
		std::cout << count << " sunflowers with " << petals << " petals" << std::endl;
	}
	std::cout << "kinds: " << by_petals.size() << '\n';
}
