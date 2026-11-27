#include "farm.hpp"
#include <iostream>
#include <string>
#include <vector>
using namespace std;

// With using namespace std the std:: prefix is optional.
int main() {
	vector<string> names = {"carrot", "pumpkin"};
	string all;
	for (const string& n : names) {
		all += n + " ";
	}
	cout << all << endl;
	cout << min(3, get_world_size()) << " " << to_string(max(1, 2)) << endl;
	return 0;
}
