#include "farm.h"
#include <stdio.h>
#include <math.h>

int main(void) {
	double x = 2.5;
	double y = x * 4;
	printf("%g %g %g\n", x, y, y / 8);
	printf("%g %g %g %g\n", floor(2.75), ceil(2.25), sqrt(16.0), pow(2, 10));
	printf("%g %g\n", fabs(-1.5), fmod(7.5, 2));
	double acc = 0;
	for (int i = 0; i < 4; i++) {
		acc += 0.25;
	}
	printf("%g %d\n", acc, (int) (acc * 3));
	printf("%g %g\n", fmin(1.5, -2.5), fmax(1.5, 4));
	return 0;
}
