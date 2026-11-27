// ERROR: use str_eq(a, b) or strcmp(a, b) == 0
// AT: ==
#include "farm.h"
#include <stdio.h>

int main(void) {
	const char *name = "farmer";
	if (name == "farmer") {
		harvest();
	}
}
