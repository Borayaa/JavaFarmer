// ERROR: identifiers starting with __j are reserved by JavaFarmer
// AT: __jvalue
#include "farm.h"
#include <stdio.h>

int __jvalue = 3;

int main(void) {
	return 0;
}
