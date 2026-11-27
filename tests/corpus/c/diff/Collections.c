#include "farm.h"
#include <stdio.h>

// farm.h's List / Set / Map behave like the game's lists, sets and dictionaries.
int main(void) {
	List(int) xs = list_of(int, 5, 3, 8);
	list_add(xs, 1);
	list_insert(xs, 1, 9);
	printf("%s len %d\n", str_of(xs), list_len(xs));
	list_set(xs, 0, list_get(xs, 0) * 10);
	int last = list_pop(xs);
	int second = list_remove_at(xs, 1);
	list_remove(xs, 3);
	printf("%s %d %d\n", str_of(xs), last, second);
	printf("%s %s %d %d\n", str_of(list_contains(xs, 8)), str_of(list_contains(xs, 3)), list_index_of(xs, 8), list_index_of(xs, 42));
	List(int) copy = list_copy(xs);
	list_add(copy, 100);
	List(int) alias = xs;
	list_add(alias, 7);
	printf("%s %s\n", str_of(xs), str_of(copy));
	int sum = 0;
	for_each (int x, copy) {
		sum += x;
	}
	printf("sum %d\n", sum);
	Set(int) seen = set_new(int);
	for (int i = 0; i < 10; i++) {
		set_add(seen, i % 4);
	}
	set_remove(seen, 2);
	printf("set %d %s %s\n", set_len(seen), str_of(set_contains(seen, 3)), str_of(set_contains(seen, 2)));
	Map(string, int) stock = map_of(string, int, {"hay", 3}, {"wood", 5});
	map_put(stock, "carrot", 1);
	map_put(stock, "hay", map_get(stock, "hay") + 10);
	printf("%d %d %d %s\n", map_get(stock, "hay"), map_get_or(stock, "gold", -1), map_len(stock), str_of(map_has(stock, "wood")));
	int removed = map_remove(stock, "wood");
	for_each (string k, stock) {
		printf("%s=%d\n", k, map_get(stock, k));
	}
	List(int) values = map_values(stock);
	List(string) keys = map_keys(stock);
	printf("%d %d %d %s\n", removed, list_len(values), list_len(keys), list_get(keys, 0));
	list_clear(values);
	printf("%s %s\n", str_of(list_is_empty(values)), str_of(map_is_empty(stock)));
	return 0;
}
