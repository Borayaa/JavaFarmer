// ERROR: fallthrough into the next case is not supported in JavaFarmer
// AT: case
#include "farm.hpp"
#include <iostream>
#include <vector>

int main() {
	switch (get_pos_x()) {
		case 0:
			move(North);
		case 1:
			move(East);
			break;
	}
}
