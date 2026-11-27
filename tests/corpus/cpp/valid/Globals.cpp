#include "farm.hpp"
#include <iostream>
#include <string>

constexpr int SIZE = 4;
const std::string greeting = "hello farm";
int rounds = get_world_size() / 2;
double total = 0;

static void mark() {
	total += 0.5;
}

int main() {
	std::cout << greeting << std::endl;
	for (int r = 0; r < rounds; r++) {
		for (int i = 0; i < SIZE; i++) {
			mark();
			move(North);
		}
		move(East);
	}
	std::cout << "total " << total << std::endl;
}
