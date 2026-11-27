// do...while runs the body at least once.
let tries = 0;
do {
	tries++;
	if (canHarvest()) {
		harvest();
	}
} while (tries < 3 && !canHarvest());
let k = 10;
do k -= 3; while (k > 0);
quickPrint("tries", tries, "k", k);
