// ERROR: join strings with str_concat(a, b)
// AT: +
#include "farm.h"
#include <stdio.h>

int main(void) {
	const char *s = "a" + "b";
}
