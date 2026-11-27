// ERROR: goto is not supported in JavaFarmer
#include "farm.h"
#include <stdio.h>

int main(void) {
	goto end;
end:
	return 0;
}
