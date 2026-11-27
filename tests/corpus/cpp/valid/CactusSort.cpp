#include "farm.hpp"
#include <algorithm>
#include <iostream>
#include <vector>

// Sort a row of cacti with bubble sort using measure() and swap().
void sort_row() {
	int n = get_world_size();
	bool swapped = true;
	while (swapped) {
		swapped = false;
		for (int i = 0; i < n - 1; i++) {
			int here = measure();
			int next = measure(East);
			if (here > next) {
				swap(East);
				swapped = true;
			}
			move(East);
		}
		for (int i = 0; i < n - 1; i++) {
			move(West);
		}
	}
}

int main() {
	const int size = get_world_size();
	std::vector<int> heights(size, 0);
	for (int x = 0; x < size; x++) {
		if (get_ground_type() != Grounds::Soil) {
			till();
		}
		plant(Entities::Cactus);
		heights[x] = static_cast<int>(measure());
		move(East);
	}
	int tallest = 0;
	for (int h : heights) {
		tallest = std::max(tallest, h);
	}
	std::cout << "tallest " << tallest << std::endl;
	sort_row();
	harvest();
}
