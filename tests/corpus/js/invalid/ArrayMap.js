// ERROR: '.map()' is not supported in JavaFarmer (use a for...of loop)
// AT: map
const xs = [1, 2];
const ys = xs.map((x) => x * 2);
