#include "farm.h"
#include <stdio.h>

// Count the sunflowers by petals, then harvest the ones with the most petals first.
int main(void) {
	Map(int, int) by_petals = map_new(int, int);
	int n = get_world_size();
	for (int x = 0; x < n; x++) {
		for (int y = 0; y < n; y++) {
			if (get_ground_type() != Grounds.Soil) till();
			plant(Entities.Sunflower);
			int petals = (int) measure();
			map_put(by_petals, petals, map_get_or(by_petals, petals, 0) + 1);
			move(North);
		}
		move(East);
	}
	for (int petals = 15; petals >= 7; petals--) {
		if (!map_has(by_petals, petals)) {
			continue;
		}
		printf("%d sunflowers with %d petals\n", map_get(by_petals, petals), petals);
	}
	for_each (int p, by_petals) {
		quick_print("petals %d", p);
	}
	printf("kinds: %d\n", map_len(by_petals));
	return 0;
}
