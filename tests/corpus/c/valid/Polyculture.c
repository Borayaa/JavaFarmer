#include "farm.h"
#include <stdio.h>

// Plant what each plant's companion wants; remember the wishes by position.
int main(void) {
	Map(Position, Entity) wishes = map_new(Position, Entity);
	int n = get_world_size();
	Entity choices[] = {Entities.Grass, Entities.Bush, Entities.Tree, Entities.Carrot};
	for (int step = 0; step < n * n; step++) {
		Entity want = choices[step % 4];
		if (want == Entities.Carrot && get_ground_type() != Grounds.Soil) {
			till();
		}
		plant(want);
		Companion c = get_companion();
		if (c != NULL) {
			map_put(wishes, c->position, c->entity);
			quick_print("wants %s at %d %d", str_of(c->entity), c->x, c->position.y);
		}
		if (get_pos_y() == n - 1) {
			move(East);
		}
		move(North);
	}
	Position origin = (Position){0, 0};
	if (map_has(wishes, origin)) {
		printf("origin wants %s\n", str_of(map_get(wishes, origin)));
	}
	printf("wishes: %d\n", map_len(wishes));
	return 0;
}
