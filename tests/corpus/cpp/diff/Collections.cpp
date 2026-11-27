#include "farm.hpp"
#include <algorithm>
#include <map>
#include <print>
#include <set>
#include <string>
#include <vector>

// std::vector / std::map / std::set behave like the game's lists, dictionaries and sets.
int sum(std::vector<int> xs) {
	int s = 0;
	for (int x : xs) s += x;
	xs.push_back(1000);   // a copy: the caller's vector does not change
	return s;
}

void add_one(std::vector<int>& xs) {
	xs.push_back(1);
}

int main() {
	std::vector<int> xs = {5, 3, 8};
	xs.push_back(1);
	xs.insert(xs.begin() + 1, 9);
	std::println("{} size {}", xs, xs.size());
	xs[0] = xs[0] * 10;
	xs.pop_back();
	xs.erase(xs.begin() + 1);
	std::println("{} {} {}", xs, xs.front(), xs.back());
	std::println("{} {}", std::ranges::contains(xs, 8), std::find(xs.begin(), xs.end(), 42) != xs.end());
	std::vector<int> copy = xs;
	copy.push_back(100);
	add_one(xs);
	std::println("{} {} {}", xs, copy, sum(copy));
	std::println("{}", copy);
	std::set<int> seen;
	for (int i = 0; i < 10; i++) {
		seen.insert(i % 4);
	}
	seen.erase(2);
	std::println("set {} {} {}", seen.size(), seen.contains(3), seen.count(2) > 0);
	// std::map visits its keys in sorted order, the game in insertion order: keep both the same here
	std::map<std::string, int> stock = {{"carrot", 0}, {"hay", 3}, {"wood", 5}};
	stock["carrot"] = 1;
	stock["hay"] += 10;
	std::println("{} {} {}", stock.at("hay"), stock.size(), stock.contains("wood"));
	stock.erase("wood");
	for (const auto& [name, count] : stock) {
		std::println("{}={}", name, count);
	}
	std::vector<std::vector<int>> grid(3, std::vector<int>(2, 7));
	grid[1][0] = 4;
	std::println("{} {} {}", grid[1][0], grid[2][1], grid.size());
	xs.clear();
	std::println("{}", xs.empty());
}
