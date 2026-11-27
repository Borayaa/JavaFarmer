#include "farm.hpp"
#include <map>
#include <print>
#include <vector>

// Plant what each plant's companion wants; remember the wishes by position.
int main() {
	std::map<Position, Entity> wishes;
	int n = get_world_size();
	std::vector<Entity> choices = {Entities::Grass, Entities::Bush, Entities::Tree, Entities::Carrot};
	for (int step = 0; step < n * n; step++) {
		Entity want = choices[step % 4];
		if (want == Entities::Carrot && get_ground_type() != Grounds::Soil) {
			till();
		}
		plant(want);
		auto c = get_companion();
		if (c) {
			wishes[c->position] = c->entity;
			auto [kind, where] = *c;
			quick_print("wants {} at {} {}", kind, where.x, where.y);
		}
		if (get_pos_y() == n - 1) {
			move(East);
		}
		move(North);
	}
	if (wishes.contains(Position{0, 0})) {
		std::println("origin wants {}", wishes.at(Position{0, 0}));
	}
	std::println("wishes: {}", wishes.size());
}
