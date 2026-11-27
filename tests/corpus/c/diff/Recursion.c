#include "farm.h"
#include <stdio.h>

int fact(int n);

int fib(int n) {
	return n < 2 ? n : fib(n - 1) + fib(n - 2);
}

int gcd(int a, int b) {
	if (b == 0) {
		return a;
	}
	return gcd(b, a % b);
}

int hanoi(int disks, int from, int to, int via) {
	if (disks == 0) return 0;
	int moves = hanoi(disks - 1, from, via, to);
	moves += 1;
	moves += hanoi(disks - 1, via, to, from);
	return moves;
}

bool is_even(int n);
bool is_odd(int n) { return n == 0 ? false : is_even(n - 1); }
bool is_even(int n) { return n == 0 ? true : is_odd(n - 1); }

int main(void) {
	for (int i = 0; i <= 10; i++) {
		printf("fib(%d) = %d\n", i, fib(i));
	}
	printf("fact(6) = %d\n", fact(6));
	printf("gcd = %d %d\n", gcd(84, 36), gcd(17, 5));
	printf("hanoi(5) = %d\n", hanoi(5, 1, 3, 2));
	printf("%s %s\n", str_of(is_even(10)), str_of(is_odd(7)));
	return 0;
}

int fact(int n) {
	if (n <= 1) return 1;
	return n * fact(n - 1);
}
