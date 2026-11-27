// ERROR: switch quantity not an integer
// AT: s
#include "farm.hpp"
#include <iostream>
#include <vector>

#include <string>

int main() {
	std::string s = "a";
	switch (s) {
		default:
			break;
	}
}
