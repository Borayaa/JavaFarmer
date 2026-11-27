// ERROR: no matching function for call to 'to_string(bool)'
// AT: can_harvest()
#include "farm.hpp"
#include <iostream>
#include <vector>

#include <string>

int main() {
	std::string s = std::to_string(can_harvest());
}
