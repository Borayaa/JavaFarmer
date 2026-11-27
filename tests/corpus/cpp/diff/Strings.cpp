#include "farm.hpp"
#include <iostream>
#include <string>

std::string repeat(const std::string& s, int times) {
	std::string res;
	for (int i = 0; i < times; i++) {
		res += s;
	}
	return res;
}

int main() {
	std::string a = "farm";
	std::string b = a + "er";
	std::cout << a << " " << b << " " << b.size() << std::endl;
	std::cout << "[" << repeat("ab", 3) << "]" << std::endl;
	bool same = a == "farm";
	std::cout << std::boolalpha << same << std::endl;
	std::string n = "n=" + std::to_string(42);
	std::cout << n << std::endl;
	if (b.find("rme") != std::string::npos && !b.empty()) {
		std::cout << "found" << std::endl;
	}
	std::string empty;
	if (empty.empty()) {
		std::cout << "empty" << std::endl;
	}
}
