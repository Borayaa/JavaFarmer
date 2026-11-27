#include "farm.hpp"
#include <iostream>

// Functions and globals of the other code windows are declared, then used like local ones.
void plant_row(Entity e);
int harvest_row();
extern int rows_done;

int main() {
	int total = 0;
	for (int i = 0; i < get_world_size(); i++) {
		plant_row(Entities::Bush);
		total += harvest_row();
		move(East);
	}
	std::cout << "harvested " << total << " in " << rows_done << " rows" << std::endl;
}
