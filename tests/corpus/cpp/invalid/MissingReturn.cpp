// ERROR: control reaches end of non-void function 'twice'
// AT: }
#include "farm.hpp"
#include <iostream>
#include <vector>

int twice(int x) {
	if (x > 0) {
		return x * 2;
	}
}

int main() {
	return twice(2);
}
