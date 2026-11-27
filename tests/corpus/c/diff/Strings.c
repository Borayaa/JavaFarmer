#include "farm.h"
#include <stdio.h>
#include <string.h>

const char *repeat(const char *s, int times) {
	const char *res = "";
	for (int i = 0; i < times; i++) {
		res = str_concat(res, s);
	}
	return res;
}

int main(void) {
	const char *a = "farm";
	const char *b = str_concat(a, "er");
	printf("%s %s %d\n", a, b, (int) strlen(b));
	printf("[%s]\n", repeat("ab", 3));
	bool same = strcmp(a, b) == 0;
	printf("%s %s\n", str_of(str_eq(a, "farm")), str_of(same));
	printf("%s\n", str_concat("n=", str_of(42)));
	printf("100%% done, %s\n", str_of(str_contains(b, "rme")));
	puts("puts adds a newline");
	const char *empty = "";
	if (strlen(empty) == 0) {
		printf("empty\n");
	}
	return 0;
}
