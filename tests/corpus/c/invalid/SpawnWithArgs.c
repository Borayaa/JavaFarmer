// ERROR: the function given to spawn_drone cannot take parameters
// AT: work
#include "farm.h"
#include <stdio.h>

void work(int column) {
	move(North);
}

int main(void) {
	spawn_drone(work);
}
