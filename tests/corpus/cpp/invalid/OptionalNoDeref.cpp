// ERROR: cannot iterate over std::optional
// AT: cost
#include "farm.hpp"
#include <iostream>
#include <vector>

int main() {
	auto cost = get_cost(Unlocks::Speed);
	for (auto& [item, n] : cost) {
	}
}
