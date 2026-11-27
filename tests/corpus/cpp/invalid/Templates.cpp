// ERROR: template declarations are not supported in JavaFarmer
// AT: template
#include "farm.hpp"
#include <iostream>
#include <vector>

template <class T> T twice(T x) { return x + x; }

int main() {
}
