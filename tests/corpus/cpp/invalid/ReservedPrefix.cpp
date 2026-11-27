// ERROR: identifiers starting with __j are reserved by JavaFarmer
// AT: __jvalue
#include "farm.hpp"
#include <iostream>
#include <vector>

int __jvalue = 3;

int main() {
}
