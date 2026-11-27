// ERROR: expected expression before '[' token
// AT: [
#include "farm.h"
#include <stdio.h>

int main(void) {
	auto f = [] { harvest(); };
}
