#include "farm.h"
#include <stdio.h>

void sort(List(int) xs) {
	int n = list_len(xs);
	for (int i = 0; i < n; i++) {
		for (int j = 0; j + 1 < n - i; j++) {
			if (list_get(xs, j) > list_get(xs, j + 1)) {
				int t = list_get(xs, j);
				list_set(xs, j, list_get(xs, j + 1));
				list_set(xs, j + 1, t);
			}
		}
	}
}

int find(List(int) xs, int value) {
	int lo = 0, hi = list_len(xs) - 1;
	while (lo <= hi) {
		int mid = (lo + hi) / 2;
		int v = list_get(xs, mid);
		if (v == value) return mid;
		if (v < value) lo = mid + 1;
		else hi = mid - 1;
	}
	return -1;
}

int main(void) {
	List(int) xs = list_of(int, 9, 4, 7, 1, 8, 2);
	sort(xs);
	printf("%s\n", str_of(xs));
	printf("%d %d %d\n", find(xs, 7), find(xs, 1), find(xs, 5));
	int collatz = 27, steps = 0;
	while (collatz != 1) {
		collatz = collatz % 2 == 0 ? collatz / 2 : 3 * collatz + 1;
		steps++;
	}
	printf("collatz steps %d\n", steps);
	return 0;
}
