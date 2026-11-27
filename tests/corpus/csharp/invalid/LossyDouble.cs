// NumItems returns a double: an int variable needs a cast.
// ERROR: CS0266: Cannot implicitly convert type 'double' to 'int'. An explicit conversion exists (are you missing a cast?)
// AT: NumItems(Items.Hay)
int hay = NumItems(Items.Hay);
