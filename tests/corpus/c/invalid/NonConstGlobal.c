// ERROR: initializer element is not constant
// AT: get_world_size()
#include "farm.h"
#include <stdio.h>

int size = get_world_size();

int main(void) {
	return 0;
}
