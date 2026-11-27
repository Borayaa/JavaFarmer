// ERROR: assignment of read-only variable 'n'
// AT: n
#include "farm.hpp"
#include <iostream>
#include <vector>

int main() {
	const int n = get_world_size();
	n = 3;
}
