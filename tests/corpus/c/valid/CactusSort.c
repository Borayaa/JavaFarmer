#include "farm.h"
#include <stdio.h>

// Sort a row of cacti with bubble sort using measure() and swap().
void sort_row(void) {
	int n = get_world_size();
	bool swapped = true;
	while (swapped) {
		swapped = false;
		for (int i = 0; i < n - 1; i++) {
			int here = (int) measure();
			int next = (int) measure(East);
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

int main(void) {
	const int size = get_world_size();
	int heights[size];
	for (int x = 0; x < size; x++) {
		if (get_ground_type() != Grounds.Soil) {
			till();
		}
		plant(Entities.Cactus);
		heights[x] = (int) measure();
		move(East);
	}
	int tallest = 0;
	for (int x = 0; x < size; x++) {
		tallest = max(tallest, heights[x]);
	}
	printf("tallest %d\n", tallest);
	sort_row();
	harvest();
	return 0;
}
