// ERROR: 'std::vector<int>' has no member named 'add'
// AT: add
#include "farm.hpp"
#include <iostream>
#include <vector>

int main() {
	std::vector<int> xs;
	xs.add(3);
}
