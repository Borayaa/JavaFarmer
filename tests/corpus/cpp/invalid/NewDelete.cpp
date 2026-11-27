// ERROR: new is not supported in JavaFarmer
// AT: new
#include "farm.hpp"
#include <iostream>
#include <vector>

int main() {
	int* p = new int(3);
}
