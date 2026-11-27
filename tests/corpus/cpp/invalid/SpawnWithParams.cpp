// ERROR: the function given to spawn_drone cannot take parameters
#include "farm.hpp"
#include <iostream>
#include <vector>

int main() {
	spawn_drone([&](int x) { move(North); });
}
