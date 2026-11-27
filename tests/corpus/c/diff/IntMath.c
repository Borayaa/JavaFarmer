#include "farm.h"
#include <stdio.h>

// Integer division and remainder truncate toward zero (also with negative numbers).
int main(void) {
	int values[] = {7, -7, 13, -13, 0};
	int divisors[] = {2, -2, 5, -5};
	for (int i = 0; i < 5; i++) {
		for (int j = 0; j < 4; j++) {
			int a = values[i], b = divisors[j];
			printf("%d / %d = %d, %d %% %d = %d\n", a, b, a / b, a, b, a % b);
		}
	}
	int x = 17;
	x /= 3;
	x *= -2;
	x %= 4;
	x -= 10;
	printf("x = %d\n", x);
	int t = (int) -7.9;
	int u = (int) 7.9;
	double h = 7 / 2;
	double r = 7 / 2.0;
	int k = 10;
	k += 2.5;
	printf("%d %d %g %g %d\n", t, u, h, r, k);
	printf("%d %d %d\n", abs(-4), min(3, -3), max(2, 9));
	return 0;
}
