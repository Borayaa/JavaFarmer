#include "farm.h"
#include <stdio.h>

const char *day_name(int d) {
	switch (d) {
		case 0: return "sun";
		case 1: return "mon";
		case 6: return "sat";
		default: return "work";
	}
}

int main(void) {
	for (int i = -1; i < 8; i++) {
		int kind = 0;
		switch (i % 4) {
			case 0:
				kind = 10;
				break;
			case 1:
			case -1:
				kind = 20;
				break;
			case 2: {
				int twice = i * 2;
				kind = twice;
				break;
			}
			default:
				kind = -1;
		}
		printf("%d -> %d %s\n", i, kind, day_name(i));
	}
	int total = 0;
	for (int i = 0; i < 6; i++) {
		switch (i) {
			case 3:
				continue;
			default:
				total += i;
		}
	}
	printf("total %d\n", total);
	return 0;
}
