#include "farm.h"
#include <stdio.h>

#define SIZE 4
#define GREETING "hello farm"

const int rounds = 3;
static int visited[SIZE][SIZE];
double total = 0;

static void mark(void) {
	int x = get_pos_x() % SIZE;
	int y = get_pos_y() % SIZE;
	visited[x][y] += 1;
	total += 0.5;
}

int main(void) {
	puts(GREETING);
	for (int r = 0; r < rounds; r++) {
		for (int i = 0; i < SIZE; i++) {
			mark();
			move(North);
		}
		move(East);
	}
	printf("visited %d times, total %g\n", visited[0][0], total);
	return 0;
}
