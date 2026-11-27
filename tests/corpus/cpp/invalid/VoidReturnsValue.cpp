// ERROR: return-statement with a value, in function returning 'void'
// AT: return 1;
#include "farm.hpp"
#include <iostream>
#include <vector>

void f() {
	return 1;
}

int main() {
	f();
}
