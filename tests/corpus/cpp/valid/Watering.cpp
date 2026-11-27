#include "farm.hpp"

// Water dry ground and fertilize slow trees.
void care() {
	while (get_water() < 0.75 && num_items(Items::Water) >= 1) {
		use_item(Items::Water);
	}
	if (get_entity_type() == Entities::Tree && !can_harvest() && num_items(Items::Fertilizer) > 10) {
		use_item(Items::Fertilizer, 1);
	}
}

int main() {
	int n = get_world_size();
	while (true) {
		for (int i = 0; i < n; i++) {
			care();
			if (can_harvest()) {
				harvest();
				plant(Entities::Tree);
			}
			move(North);
		}
		move(East);
	}
}
