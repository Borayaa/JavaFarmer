// ERROR: bitwise operators are not supported in JavaFarmer
// AT: &
#include "farm.h"
#include <stdio.h>

int main(void) {
	int x = get_pos_x() & 1;
}
