#include "farm.hpp"
#include <iostream>
#include <string>

std::string day_name(int d) {
	switch (d) {
		case 0: return "sun";
		case 1: return "mon";
		case 6: return "sat";
		default: return "work";
	}
}

int main() {
	for (int i = -1; i < 8; i++) {
		int kind = 0;
		switch (i % 4) {
			case 0:
				kind = 10;
				break;
			case 1:
			case -1:
				kind = 20;
				break;
			case 2: {
				int twice = i * 2;
				kind = twice;
				break;
			}
			default:
				kind = -1;
		}
		std::cout << i << " -> " << kind << " " << day_name(i) << std::endl;
	}
}
