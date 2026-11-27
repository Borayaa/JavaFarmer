// ERROR: bitwise operators are not supported in JavaFarmer
// AT: &
#include "farm.hpp"
#include <iostream>
#include <vector>

int main() {
	int x = get_pos_x() & 1;
}
