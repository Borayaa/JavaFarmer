#include "farm.hpp"

// The first program: cut grass forever.
int main() {
	while (true) {
		if (can_harvest()) {
			harvest();
		}
		move(North);
	}
}
