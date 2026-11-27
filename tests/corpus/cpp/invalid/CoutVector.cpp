// ERROR: no match for 'operator<<' (operand types are 'std::ostream' and 'std::vector<int>')
// AT: xs
#include "farm.hpp"
#include <iostream>
#include <vector>

int main() {
	std::vector<int> xs = {1, 2};
	std::cout << xs << std::endl;
}
