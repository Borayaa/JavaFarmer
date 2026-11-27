// ERROR: 'return' with a value, in function returning void
// AT: return 1;
#include "farm.h"
#include <stdio.h>

void f(void) {
	return 1;
}

int main(void) {
	f();
}
