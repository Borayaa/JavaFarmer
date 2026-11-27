#include "farm.hpp"
#include <print>
#include <vector>

void sort(std::vector<int>& xs) {
	int n = xs.size();
	for (int i = 0; i < n; i++) {
		for (int j = 0; j + 1 < n - i; j++) {
			if (xs[j] > xs[j + 1]) {
				int t = xs[j];
				xs[j] = xs[j + 1];
				xs[j + 1] = t;
			}
		}
	}
}

int find(const std::vector<int>& xs, int value) {
	int lo = 0, hi = xs.size() - 1;
	while (lo <= hi) {
		int mid = (lo + hi) / 2;
		if (xs[mid] == value) return mid;
		if (xs[mid] < value) lo = mid + 1;
		else hi = mid - 1;
	}
	return -1;
}

int main() {
	std::vector<int> xs = {9, 4, 7, 1, 8, 2};
	sort(xs);
	std::println("{}", xs);
	std::println("{} {} {}", find(xs, 7), find(xs, 1), find(xs, 5));
	int collatz = 27, steps = 0;
	while (collatz != 1) {
		collatz = collatz % 2 == 0 ? collatz / 2 : 3 * collatz + 1;
		steps++;
	}
	std::println("collatz steps {}", steps);
	std::vector<bool> composite(40, false);
	int primes = 0;
	for (int i = 2; i < 40; i++) {
		if (composite[i]) continue;
		primes++;
		for (int j = i * i; j < 40; j += i) composite[j] = true;
	}
	std::println("{} primes", primes);
}
