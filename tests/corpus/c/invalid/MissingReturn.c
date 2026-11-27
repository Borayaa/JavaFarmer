// ERROR: control reaches end of non-void function 'twice'
// AT: }
#include "farm.h"
#include <stdio.h>

int twice(int x) {
	if (x > 0) {
		return x * 2;
	}
}

int main(void) {
	return twice(2);
}
