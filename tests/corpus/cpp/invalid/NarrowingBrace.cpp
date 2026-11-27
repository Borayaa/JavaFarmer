// ERROR: narrowing conversion of 'get_time()' from 'double' to 'int'
// AT: get_time()
#include "farm.hpp"
#include <iostream>
#include <vector>

int main() {
	int x{get_time()};
}
