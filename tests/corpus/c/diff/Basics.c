#include "farm.h"
#include <stdio.h>
int fib(int n) { if (n < 2) return n; return fib(n - 1) + fib(n - 2); }
int main(void) {
	List(int) xs = list_of(int, 3, 1, 2);
	list_add(xs, 10);
	int total = 0;
	for_each (int x, xs) { if (x == 2) continue; total += x; }
	Map(string, int) ages = map_new(string, int);
	map_put(ages, "bob", 4);
	printf("%d %d %s %d\n", total, fib(10), str_of(xs), map_get(ages, "bob"));
	printf("%d %d %g\n", -7 / 2, -7 % 2, 7.0 / 2);
	printf("%s\n", str_of(true));
	printf("partial ");
	printf("line\n");
	return 0;
}
