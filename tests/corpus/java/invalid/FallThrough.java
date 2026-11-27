// ERROR: falling through to the next case is not supported in JavaFarmer
// AT: case 2:
void main() {
    int x = getPosX();
    switch (x) {
        case 1:
            harvest();
        case 2:
            till();
            break;
    }
}
