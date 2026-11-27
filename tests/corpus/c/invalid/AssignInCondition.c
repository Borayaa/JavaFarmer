// ERROR: assignment as a value is not supported in JavaFarmer
// AT: =
#include "farm.h"
#include <stdio.h>

int main(void) {
	int x = 0;
	if (x = 1) {
		harvest();
	}
}
