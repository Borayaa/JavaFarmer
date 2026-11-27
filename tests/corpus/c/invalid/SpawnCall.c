// ERROR: spawn_drone needs the function itself: write spawn_drone(work) without ()
// AT: work()
#include "farm.h"
#include <stdio.h>

void work(void) {
	move(North);
}

int main(void) {
	spawn_drone(work());
}
