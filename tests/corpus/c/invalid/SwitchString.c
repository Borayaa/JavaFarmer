// ERROR: switch quantity not an integer
// AT: s
#include "farm.h"
#include <stdio.h>

int main(void) {
	const char *s = "a";
	switch (s) {
		default:
			break;
	}
}
