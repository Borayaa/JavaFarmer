#include "farm.h"
#include <stdio.h>

int main(void) {
	int sum = 0;
	for (int i = 0; i < 10; i++) {
		if (i % 3 == 0) continue;
		if (i == 8) break;
		sum += i;
	}
	printf("sum %d\n", sum);
	int n = 0;
	do {
		n += 3;
	} while (n < 20);
	printf("n %d\n", n);
	int count = 0;
	while (true) {
		count++;
		if (count >= 5) break;
	}
	printf("count %d\n", count);
	for (int a = 0, b = 10; a < b; a++, b--) {
		printf("%d-%d\n", a, b);
	}
	for (int i = 5; i > 0; i--) {
		for (int j = 0; j < i; j++) {
			if (j == 2) continue;
			printf("%d", j);
		}
		printf("\n");
	}
	int k = 0;
	for (;;) {
		++k;
		if (k == 4) {
			break;
		}
	}
	printf("k %d\n", k);
	return 0;
}
