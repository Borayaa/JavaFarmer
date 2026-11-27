// A loop variable may not reuse the name of an outer variable.
// ERROR: CS0136: A local or parameter named 'i' cannot be declared in this scope because that name is used in an enclosing local scope to define a local or parameter
// AT: i
int i = 0;
for (int i = 0; i < 3; i++)
{
	Move(North);
}
