// ERROR: variable x is already defined in method main()
// AT: x
void main() {
    int x = 1;
    if (x > 0) {
        int x = 2;
    }
}
