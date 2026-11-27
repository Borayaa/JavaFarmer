// ERROR: redeclaration of 'int x'
// AT: x
#include "farm.hpp"
#include <iostream>
#include <vector>

int main() {
	int x = 1;
	int x = 2;
}
