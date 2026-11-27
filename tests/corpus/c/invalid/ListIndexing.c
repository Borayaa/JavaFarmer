// ERROR: a List is not an array: read elements with list_get(xs, i)
// AT: xs[0]
#include "farm.h"
#include <stdio.h>

int main(void) {
	List(int) xs = list_of(int, 1, 2);
	int a = xs[0];
}
