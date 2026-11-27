// ERROR: incompatible type for argument 2 of 'list_add'
// AT: Entities.Bush
#include "farm.h"
#include <stdio.h>

int main(void) {
	List(int) xs = list_new(int);
	list_add(xs, Entities.Bush);
}
