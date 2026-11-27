// ERROR: i++ as a value is not supported in JavaFarmer
// AT: i++
#include "farm.hpp"
#include <iostream>
#include <vector>

int main() {
	int i = 0;
	int j = i++;
}
