#include "farm.h"
#include <stdio.h>

#define LIMIT 60

int main(void) {
	bool composite[LIMIT + 1] = {false};
	int count = 0;
	for (int i = 2; i <= LIMIT; i++) {
		if (composite[i]) {
			continue;
		}
		count++;
		printf("%d\n", i);
		for (int j = i * i; j <= LIMIT; j += i) {
			composite[j] = true;
		}
	}
	printf("%d primes\n", count);
	return 0;
}
