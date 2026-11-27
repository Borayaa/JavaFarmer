// ERROR: i++ as a value is not supported in JavaFarmer
// AT: i++
#include "farm.h"
#include <stdio.h>

int main(void) {
	int i = 0;
	int j = i++;
}
