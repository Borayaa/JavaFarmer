// ERROR: convert the number with std::to_string(x)
// AT: +
#include "farm.hpp"
#include <iostream>
#include <vector>

#include <string>

int main() {
	std::string s = "x";
	std::string t = s + get_pos_x();
}
