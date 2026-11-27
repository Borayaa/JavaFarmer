// ERROR: fallthrough into the next case is not supported in JavaFarmer
// AT: case
#include "farm.h"
#include <stdio.h>

int main(void) {
	switch (get_pos_x()) {
		case 0:
			move(North);
		case 1:
			move(East);
			break;
	}
}
