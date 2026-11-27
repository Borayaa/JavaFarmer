#include "farm.hpp"
#include <iostream>
#include <print>

// Text without a line break is kept until the line is finished.
int main() {
	for (int n = 1; n <= 4; n++) {
		for (int i = 1; i <= n; i++) {
			std::cout << i * n << " ";
		}
		std::cout << std::endl;
	}
	std::print("a");
	std::print("b{}", 1);
	std::println("c");
	std::cout << "two\nlines\n";
}
