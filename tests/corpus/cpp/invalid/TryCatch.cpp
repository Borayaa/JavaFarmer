// ERROR: try is not supported in JavaFarmer
#include "farm.hpp"
#include <iostream>
#include <vector>

int main() {
	try {
		harvest();
	} catch (...) {
	}
}
