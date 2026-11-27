#include "farm.h"
#include <stdio.h>

// Remember which kinds of plants we have seen.
int main(void) {
	Set(Entity) seen = set_new(Entity);
	Set(Direction) blocked = set_of(Direction, North, West);
	for (int i = 0; i < get_world_size(); i++) {
		Entity e = get_entity_type();
		if (e != NULL && !set_contains(seen, e)) {
			set_add(seen, e);
		}
		if (!set_contains(blocked, East)) {
			move(East);
		}
	}
	set_remove(blocked, West);
	for_each (Entity e, seen) {
		printf("saw %s\n", str_of(e));
	}
	printf("%d kinds, %d blocked\n", set_len(seen), set_len(blocked));
	return 0;
}
