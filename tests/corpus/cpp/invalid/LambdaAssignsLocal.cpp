// ERROR: a closure cannot assign the outer variable 'count'
#include "farm.hpp"
#include <iostream>
#include <vector>

int main() {
	int count = 0;
	spawn_drone([&]() { count++; });
}
