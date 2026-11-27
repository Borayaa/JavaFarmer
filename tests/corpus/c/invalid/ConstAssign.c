// ERROR: assignment of read-only variable 'n'
// AT: n
#include "farm.h"
#include <stdio.h>

int main(void) {
	const int n = get_world_size();
	n = 3;
}
