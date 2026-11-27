// ERROR: call of overloaded 'random()' is ambiguous
// AT: random
#include "farm.hpp"
#include <iostream>
#include <vector>

int main() {
	double r = random();
}
