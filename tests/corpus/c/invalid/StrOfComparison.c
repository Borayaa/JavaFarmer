// ERROR: in C a comparison has type int
#include "farm.h"
#include <stdio.h>

int main(void) {
	printf("%s\n", str_of(get_pos_x() == 0));
}
