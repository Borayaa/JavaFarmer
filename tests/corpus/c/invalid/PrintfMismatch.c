// ERROR: format '%d' expects argument of type 'int', but argument 2 has type 'double'
// AT: get_time()
#include "farm.h"
#include <stdio.h>

int main(void) {
	printf("%d\n", get_time());
}
