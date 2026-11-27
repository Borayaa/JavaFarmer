#include "farm.h"
#include <stdio.h>

int counter = 0;

void tick(void) {
	counter++;
}

void twice(void) {
	counter += 2;
}

void run(void (*f)(void), int times) {
	for (int i = 0; i < times; i++) {
		f();
	}
}

int main(void) {
	run(tick, 3);
	run(&twice, 2);
	void (*g)(void) = tick;
	g();
	printf("counter %d\n", counter);
	return 0;
}
