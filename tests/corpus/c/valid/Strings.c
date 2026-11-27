#include "farm.h"
#include <stdio.h>
#include <string.h>

// Text in C: string literals, str_concat, str_of, str_eq and strlen.
const char *describe(Entity e) {
	if (e == NULL) {
		return "nothing";
	}
	return str_concat("a ", str_of(e));
}

int main(void) {
	string name = "farmer";
	const char *title = str_concat(name, " #1");
	if (str_eq(name, "farmer") && strcmp(title, "farmer") != 0) {
		printf("%s (%d letters)\n", title, (int) strlen(title));
	}
	printf("under the drone: %s\n", describe(get_entity_type()));
	printf("x=%d y=%d\n", get_pos_x(), get_pos_y());
	return 0;
}
