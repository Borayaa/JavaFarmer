// ERROR: spread (...) in a call is not supported in JavaFarmer
// AT: ...values
const values = [1, 2, 3];
console.log(Math.max(...values));
