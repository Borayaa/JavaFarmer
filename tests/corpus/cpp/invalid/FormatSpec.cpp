// ERROR: format specifications like {:5} are not supported in JavaFarmer
#include "farm.hpp"
#include <iostream>
#include <vector>

#include <print>

int main() {
	std::println("{:5}", 3);
}
