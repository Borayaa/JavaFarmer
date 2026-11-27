#include "farm.hpp"
#include <iostream>
#include <set>

// Remember which kinds of plants we have seen.
int main() {
	std::set<Entity> seen;
	std::set<Direction> blocked = {North, West};
	for (int i = 0; i < get_world_size(); i++) {
		Entity e = get_entity_type();
		if (e != nullptr && !seen.contains(e)) {
			seen.insert(e);
		}
		if (blocked.count(East) == 0) {
			move(East);
		}
	}
	blocked.erase(West);
	for (const auto& e : seen) {
		std::cout << "saw " << e << std::endl;
	}
	std::cout << seen.size() << " kinds, " << blocked.size() << " blocked" << std::endl;
}
