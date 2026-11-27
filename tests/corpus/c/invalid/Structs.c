// ERROR: struct declarations are not supported in JavaFarmer
// AT: struct
#include "farm.h"
#include <stdio.h>

struct point { int x; int y; };

int main(void) {
	return 0;
}
