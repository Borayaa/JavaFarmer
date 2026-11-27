#include "farm.h"

// A library window: no main.
static int moves = 0;

void walk(int steps) {
	for (int i = 0; i < steps; i++) {
		move(North);
		moves++;
	}
}
