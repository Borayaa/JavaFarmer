// ERROR: cannot convert 'Entity' to 'Direction' in argument 1 of 'move'
// AT: Entities::Bush
#include "farm.hpp"
#include <iostream>
#include <vector>

int main() {
	move(Entities::Bush);
}
