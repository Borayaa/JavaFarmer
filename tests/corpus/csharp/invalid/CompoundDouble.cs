// x += 1.5 on an int needs a cast, like in C#.
// ERROR: CS0266: Cannot implicitly convert type 'double' to 'int'. An explicit conversion exists (are you missing a cast?)
// AT: 1.5
int x = 1;
x += 1.5;
