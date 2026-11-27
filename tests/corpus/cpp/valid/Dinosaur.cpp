#include "farm.hpp"
#include <iostream>
#include <vector>

// Wear the dinosaur hat and walk towards the apples.
std::vector<Direction> path;

bool step(Direction d) {
	if (move(d)) {
		path.push_back(d);
		return true;
	}
	return false;
}

int main() {
	change_hat(Hats::Dinosaur_Hat);
	for (int i = 0; i < 20; i++) {
		Position apple = measure();
		bool moved;
		if (get_pos_x() < apple.x) {
			moved = step(East);
		} else if (get_pos_x() > apple.x) {
			moved = step(West);
		} else if (get_pos_y() < apple.y) {
			moved = step(North);
		} else {
			moved = step(South);
		}
		if (!moved) {
			break;
		}
	}
	std::cout << "steps " << path.size() << std::endl;
	change_hat(Hats::Straw_Hat);
}
