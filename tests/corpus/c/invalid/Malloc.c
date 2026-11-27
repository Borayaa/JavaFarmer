// ERROR: 'free' is not supported in JavaFarmer
// AT: free
#include "farm.h"
#include <stdio.h>

#include <stdlib.h>

int main(void) {
	int n = 4;
	free(malloc(n));
}
