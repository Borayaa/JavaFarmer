#include "farm.hpp"
#include <iostream>

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

int power(int base, int exp = 2) {
	int r = 1;
	for (int i = 0; i < exp; i++) r *= base;
	return r;
}

int main() {
	for (int i = 0; i <= 10; i++) {
		std::cout << "fib(" << i << ") = " << fib(i) << std::endl;
	}
	std::cout << "fact(6) = " << fact(6) << std::endl;
	std::cout << gcd(84, 36) << " " << gcd(17, 5) << std::endl;
	std::cout << power(3) << " " << power(2, 10) << std::endl;
}

int fact(int n) {
	if (n <= 1) return 1;
	return n * fact(n - 1);
}
