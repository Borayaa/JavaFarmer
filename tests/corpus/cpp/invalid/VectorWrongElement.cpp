// ERROR: cannot convert 'Entity' to 'int' in argument 1 of 'push_back'
// AT: Entities::Bush
#include "farm.hpp"
#include <iostream>
#include <vector>

int main() {
	std::vector<int> xs;
	xs.push_back(Entities::Bush);
}
