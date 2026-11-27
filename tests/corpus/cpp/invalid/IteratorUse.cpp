// ERROR: iterators are only supported in
#include "farm.hpp"
#include <iostream>
#include <vector>

int main() {
	std::vector<int> xs = {1};
	auto it = xs.begin();
}
