// ERROR: redeclaration of 'x' with no linkage
// AT: x
#include "farm.h"
#include <stdio.h>

int main(void) {
	int x = 1;
	int x = 2;
}
