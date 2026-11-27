#include "farm.h"
#include <stdio.h>

// Functions and globals of the other code windows are declared, then used like local ones.
void plant_row(Entity e);
int harvest_row(void);
extern int rows_done;

int main(void) {
	int total = 0;
	for (int i = 0; i < get_world_size(); i++) {
		plant_row(Entities.Bush);
		total += harvest_row();
		move(East);
	}
	printf("harvested %d in %d rows\n", total, rows_done);
	return 0;
}
