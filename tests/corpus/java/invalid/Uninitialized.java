// ERROR: variable x might not have been initialized
// AT: x
void main() {
    int x;
    if (canHarvest()) {
        x = 1;
    }
    quickPrint(x);
}
