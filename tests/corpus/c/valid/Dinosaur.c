#include "farm.h"
#include <stdio.h>

// Wear the dinosaur hat and walk a square.
List(Direction) path;

bool step(Direction d) {
	if (move(d)) {
		list_add(path, d);
		return true;
	}
	return false;
}

int main(void) {
	path = list_new(Direction);
	change_hat(Hats.Dinosaur_Hat);
	Direction square[] = {North, East, South, West};
	for (int i = 0; i < 4; i++) {
		for (int k = 0; k < 3; k++) {
			if (!step(square[i])) {
				break;
			}
		}
	}
	printf("steps %d\n", list_len(path));
	change_hat(Hats.Straw_Hat);
	return 0;
}
