// ERROR: invalid operands to binary % (have 'double' and 'int'); use fmod(a, b) for doubles
// AT: %
#include "farm.h"
#include <stdio.h>

int main(void) {
	double x = get_time() % 2;
}
