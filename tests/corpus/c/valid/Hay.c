#include "farm.h"

// The first program: cut grass forever.
int main(void) {
	while (true) {
		if (can_harvest()) {
			harvest();
		}
		move(North);
	}
	return 0;
}
