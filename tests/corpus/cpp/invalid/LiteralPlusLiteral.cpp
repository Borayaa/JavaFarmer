// ERROR: invalid operands of types 'const char*' and 'const char*' to binary 'operator+'
// AT: +
#include "farm.hpp"
#include <iostream>
#include <vector>

#include <string>

int main() {
	std::string t = "a" + "b";
}
