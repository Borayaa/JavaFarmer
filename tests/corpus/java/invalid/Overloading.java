// ERROR: method overloading is not supported in JavaFarmer
// AT: go
void go(int n) {
}

void go(int n, int m) {
}

void main() {
    go(1);
}
