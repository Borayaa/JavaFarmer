#include "farm.hpp"
#include <iostream>

// switch on a counter, do-while and a for loop that counts down.
int main() {
	int step = 0;
	do {
		switch (step % 4) {
			case 0:
				move(North);
				break;
			case 1:
			case 2:
				move(East);
				break;
			default:
				do_a_flip();
		}
		step++;
	} while (step < 12);
	for (int i = 10; i > 0; i -= 2) {
		if (i == 4) {
			break;
		}
		std::cout << "countdown " << i << std::endl;
	}
	return 0;
}
