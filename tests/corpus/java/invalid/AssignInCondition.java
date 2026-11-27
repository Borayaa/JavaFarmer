// ERROR: using an assignment as a value is not supported in JavaFarmer
// AT: x = 5
void main() {
    int x = 0;
    if ((x = 5) > 2) {
        harvest();
    }
}
