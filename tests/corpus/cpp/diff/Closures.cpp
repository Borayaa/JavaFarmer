#include "farm.hpp"
#include <functional>
#include <iostream>

int counter = 0;

void run(const std::function<void()>& f, int times) {
	for (int i = 0; i < times; i++) {
		f();
	}
}

int main() {
	int step = 3;
	auto add = [&]() { counter += step; };
	run(add, 2);
	run([] { counter++; }, 4);
	std::function<int()> get = [&] { return counter * 2; };
	auto square = [](int x) { return x * x; };
	std::cout << counter << " " << get() << " " << square(7) << std::endl;
}
