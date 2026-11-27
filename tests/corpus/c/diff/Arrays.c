#include "farm.h"
#include <stdio.h>

#define N 5

int sum(int a[], int n) {
	int s = 0;
	for (int i = 0; i < n; i++) {
		s += a[i];
	}
	return s;
}

void fill(int a[], int n, int value) {
	for (int i = 0; i < n; i++) {
		a[i] = value + i;
	}
}

int main(void) {
	int a[N] = {4, 2};
	int len = sizeof(a) / sizeof(a[0]);
	printf("%d %d %d %d\n", len, a[0], a[1], a[4]);
	fill(a, len, 10);
	printf("sum %d\n", sum(a, len));
	int grid[3][4];
	for (int x = 0; x < 3; x++) {
		for (int y = 0; y < 4; y++) {
			grid[x][y] = x * 10 + y;
		}
	}
	printf("%d %d\n", grid[2][3], grid[1][0]);
	int small[2][2] = {{1, 2}, {3, 4}};
	small[1][1] += 10;
	printf("%d %d\n", small[0][1], small[1][1]);
	bool flags[4] = {false};
	flags[2] = true;
	int on = 0;
	for (int i = 0; i < 4; i++) {
		if (flags[i]) on++;
	}
	printf("on %d\n", on);
	int n = 6;
	int squares[n];
	for (int i = 0; i < n; i++) squares[i] = i * i;
	printf("last square %d\n", squares[n - 1]);
	return 0;
}
