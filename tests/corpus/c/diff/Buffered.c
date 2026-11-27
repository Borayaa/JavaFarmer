#include "farm.h"
#include <stdio.h>

// printf without "\n" keeps the text until a later printf finishes the line.
void row(int n) {
	for (int i = 1; i <= n; i++) {
		printf("%d ", i * n);
	}
	printf("\n");
}

int main(void) {
	for (int n = 1; n <= 4; n++) {
		row(n);
	}
	printf("a");
	printf("b");
	printf("c\nd");
	printf("e\n");
	printf("two\nlines\n");
	return 0;
}
