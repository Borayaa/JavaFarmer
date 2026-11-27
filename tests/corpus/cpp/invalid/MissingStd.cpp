// ERROR: 'vector' does not name a type; did you mean 'std::vector'?
// AT: vector
#include "farm.hpp"
#include <iostream>
#include <vector>

int main() {
	vector<int> xs;
}
