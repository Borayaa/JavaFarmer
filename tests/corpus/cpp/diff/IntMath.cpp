#include "farm.hpp"
#include <iostream>
#include <vector>

// Integer division and remainder truncate toward zero (also with negative numbers).
int main() {
	std::vector<int> values = {7, -7, 13, -13, 0};
	std::vector<int> divisors = {2, -2, 5, -5};
	for (int a : values) {
		for (int b : divisors) {
			std::cout << a << " / " << b << " = " << a / b << ", " << a << " % " << b << " = " << a % b << std::endl;
		}
	}
	int x = 17;
	x /= 3;
	x *= -2;
	x %= 4;
	x -= 10;
	std::cout << "x = " << x << std::endl;
	int t = static_cast<int>(-7.9);
	int u = (int) 7.9;
	double h = 7 / 2;
	double r = 7 / 2.0;
	int k = 10;
	k += 2.5;
	std::cout << t << " " << u << " " << h << " " << r << " " << k << std::endl;
	std::cout << std::abs(-4) << " " << std::min(3, -3) << " " << std::max(2, 9) << std::endl;
}
