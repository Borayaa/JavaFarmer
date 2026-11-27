#include "farm.hpp"
#include <print>
#include <vector>

// Check what we can afford before unlocking.
bool can_afford(Unlock u) {
	auto cost = get_cost(u);
	if (!cost) {
		return false;
	}
	for (const auto& [item, amount] : *cost) {
		if (num_items(item) < amount) {
			return false;
		}
	}
	return true;
}

int main() {
	std::vector<Unlock> wanted = {Unlocks::Speed, Unlocks::Expand, Unlocks::Plant};
	for (Unlock u : wanted) {
		if (can_afford(u) && num_unlocked(u) < 5) {
			unlock(u);
		}
	}
	auto level2 = get_cost(Unlocks::Speed, 2);
	if (level2.has_value()) {
		std::println("speed 2 needs {} items", level2->size());
	}
	change_hat(Hats::Straw_Hat);
	set_execution_speed(2);
	double t = get_time();
	quick_print("ticks {} time {}", get_tick_count(), t);
}
