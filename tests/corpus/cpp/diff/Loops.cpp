#include "farm.hpp"
#include <iostream>

int main() {
	int sum = 0;
	for (int i = 0; i < 10; i++) {
		if (i % 3 == 0) continue;
		if (i == 8) break;
		sum += i;
	}
	std::cout << "sum " << sum << std::endl;
	int n = 0;
	do {
		n += 3;
	} while (n < 20);
	std::cout << "n " << n << '\n';
	int count = 0;
	while (true) {
		count++;
		if (count >= 5) break;
	}
	std::cout << "count " << count << std::endl;
	for (int i = 5; i > 0; i--) {
		for (int j = 0; j < i; j++) {
			if (j == 2) continue;
			std::cout << j;
		}
		std::cout << std::endl;
	}
}
